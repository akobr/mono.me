using System.Runtime.CompilerServices;
using System.Threading.Channels;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Documents;
using _42.Platform.Storyteller.Sdk.Configuring.Logging;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Hosting.Catalog;
using _42.Platform.Storyteller.Sdk.Hosting.Informer;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Hosting.Subscriptions;

// The hosted informer of builder.AddSubscriptions(…). Its loop takes the batches of a change source, the polling one,
// and for each applies the changes of the subscriptions, reads the documents of the dirty keys, at most
// MaxConcurrentReads at once, and only then publishes, so every event carries its document: a new view for GetAll and
// Find, a new status, and the events for the watchers and the named options. The documents come from a private
// document service with its own transport, circuit breaker and cache, so failing tenants cannot open the breaker of
// the application's own reads. Only the loop changes what is held; readers see immutable views.
internal sealed class SubscriptionsService : BackgroundService, IStorytellerSubscriptions
{
    // The category an application filters on.
    internal const string LogCategory = "_42.Platform.Storyteller.Sdk.Hosting.Subscriptions";

    // The longest delay a CancellationTokenSource takes; anything longer is no limit.
    private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue - 1);

    private readonly SubscriptionSettings _subscription;
    private readonly ISubscriptionChangeSource _source;
    private readonly StorytellerDocuments _documents;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly List<Channel<StorytellerSubscriptionChange>> _watchers = [];
    private readonly List<Listener> _listeners = [];

    // What the loop holds, by the full key of the execution; only the loop touches it.
    private readonly Dictionary<string, Tracked> _tracked = new(StringComparer.Ordinal);

    private View _view = View.Empty;
    private StorytellerSubscriptionsStatus _status = new();

    // Completed and replaced whenever the loop published, so waiters look again.
    private TaskCompletionSource _progress = NewSignal();
    private bool _hasEnded;
    private StorytellerConfigurationException? _endFailure;

    // The failure the watchers end with: only that of a loop that broke, as a stop and a zero interval end them
    // without one.
    private StorytellerConfigurationException? _watchFailure;

    // The loop's own record of its polls.
    private bool _isReady;
    private DateTimeOffset? _lastPollSucceededAt;
    private StorytellerConfigurationException? _lastPollError;
    private int _consecutivePollFailures;

    private int _isDisposed;

    // pages: the catalog's reader, which the polling source lists and reads through. loggerFactory: the container's,
    // used when the options set none.
    public SubscriptionsService(SubscriptionSettings subscription, StorytellerSettings settings, IAnnotationPages pages, ILoggerFactory? loggerFactory)
        : this(subscription, settings, CreateSource(subscription, settings, pages, loggerFactory), loggerFactory)
    {
    }

    // source: the seam a watch of change events (424-49) or a bulk read takes.
    internal SubscriptionsService(SubscriptionSettings subscription, StorytellerSettings settings, ISubscriptionChangeSource source, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(source);

        _subscription = subscription;
        _source = source;
        _timeProvider = settings.TimeProvider;

        // A factory the container disposed before a late poll must not fail that poll.
        _logger = new SwitchableLoggerFactory(settings.LoggerFactory ?? loggerFactory).CreateLogger(LogCategory);

        // Over the Hosting settings, so over the one bound credential, but with a transport and a cache of its own. Its
        // reads log in the provider's category.
        _documents = new StorytellerDocuments(settings, loggerFactory);
    }

    public string Responsibility => _subscription.Scope.Responsibility;

    public string Context => _subscription.Scope.Context;

    public StorytellerSubscriptionsStatus Status => Volatile.Read(ref _status);

    // The private documents; tests look into their cache.
    internal StorytellerDocuments Documents => _documents;

    private bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    public async Task WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task progress;

            lock (_lock)
            {
                if (_status.IsReady)
                {
                    return;
                }

                // A copy per waiter: the poll's exception is shared, and a throw would rewrite its stack trace.
                if (_status.LastPollError is { IsTransient: false } definitive)
                {
                    throw Copy(definitive);
                }

                if (_hasEnded)
                {
                    throw _endFailure is { } failure
                        ? Copy(failure)
                        : new OperationCanceledException($"The subscriptions of {_subscription.Scope} stopped before they were ready.");
                }

                progress = _progress.Task;
            }

            await progress.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public IReadOnlyList<StorytellerSubscription> GetAll()
    {
        return Volatile.Read(ref _view).All;
    }

    public StorytellerSubscription? Find(string subject)
    {
        ArgumentNullException.ThrowIfNull(subject);

        return Volatile.Read(ref _view).BySubject.GetValueOrDefault(subject);
    }

    // The replay and the registration happen under the lock the loop publishes under, so a watcher misses no change and
    // sees none twice.
    public async IAsyncEnumerable<StorytellerSubscriptionChange> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<StorytellerSubscriptionChange>(new UnboundedChannelOptions { SingleReader = true });

        lock (_lock)
        {
            foreach (var subscription in _view.All)
            {
                channel.Writer.TryWrite(new StorytellerSubscriptionChange(StorytellerChangeKind.Added, subscription));
            }

            if (_hasEnded)
            {
                channel.Writer.TryComplete(_watchFailure is { } failure ? Copy(failure) : null);
            }
            else
            {
                _watchers.Add(channel);
            }
        }

        try
        {
            await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return change;
            }
        }
        finally
        {
            lock (_lock)
            {
                _watchers.Remove(channel);
            }
        }
    }

    // With WaitUntilReadyOnStart, the host's start waits for the first poll and its reads, up to StartTimeout on the
    // system clock, as ReadTimeout runs; a definitive failure of the poll fails the start at once.
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        await base.StartAsync(cancellationToken).ConfigureAwait(false);

        if (!_subscription.WaitUntilReadyOnStart)
        {
            return;
        }

        var startTimeout = _subscription.StartTimeout;
        using var timeout = startTimeout == Timeout.InfiniteTimeSpan || startTimeout > MaxTimeout
            ? new CancellationTokenSource()
            : new CancellationTokenSource(startTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            await WaitUntilReadyAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            var failure = Status.LastPollError;
            var reason = failure is null
                ? "the first poll and the reads of its documents did not complete."
                : $"the latest poll failed. {failure.Message}";

            throw new StorytellerConfigurationException(
                $"The subscriptions of {_subscription.Scope} were not ready within the StartTimeout of {startTimeout}: {reason}",
                _subscription.Scope.ResponsibilityKey.ToString(),
                failure?.StatusCode,
                failure?.ErrorCode,
                true,
                failure ?? (Exception)exception);
        }
    }

    // Disposing the private documents cancels their reads in flight and drops their cached secrets.
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _documents.Dispose();
            End(null);
        }
    }

    // The container disposes it once per registration that returns it.
    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        base.Dispose();
        _documents.Dispose();
        End(null);
    }

    // The named options listen to the published changes, once per change. invalidate: called under the lock the loop
    // publishes under, after the view changed and before the watchers and the waiters can see the change, so the
    // options they get next are bound to it; it must be quick and must not call back. notify: called on the loop after
    // the lock.
    internal IDisposable Listen(Action<StorytellerSubscriptionChange> invalidate, Action<StorytellerSubscriptionChange> notify)
    {
        ArgumentNullException.ThrowIfNull(invalidate);
        ArgumentNullException.ThrowIfNull(notify);

        var listener = new Listener(this, invalidate, notify);

        lock (_lock)
        {
            _listeners.Add(listener);
        }

        return listener;
    }

    // A loop that breaks is not rethrown: the subscriptions keep serving what they held, as for a failed poll, and the
    // status and the watchers report the failure, so a health check sees it.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        StorytellerConfigurationException? endFailure = null;
        var isBroken = false;

        try
        {
            await foreach (var batch in _source.ReadAsync(_subscription.Scope, stoppingToken).ConfigureAwait(false))
            {
                await ApplyAsync(batch, stoppingToken).ConfigureAwait(false);

                // The one poll of a zero interval: its failure is what a waiter gets after the end.
                endFailure = batch.Failure;
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && stoppingToken.IsCancellationRequested)
        {
            // A stop.
            endFailure = null;
        }
        catch (Exception exception)
        {
            // Not a failure of a read, which the source and the loop take as answers: a bug. Its message is not ours to
            // vouch for, so it stays in the inner exception.
            _logger.LogError(
                "The informer of the subscriptions of {Scope} stopped: it failed with {Type}. The subscriptions keep what they held, the status reports the failure and the watchers end with it.",
                _subscription.Scope.ToString(),
                exception.GetType().Name);
            endFailure = new StorytellerConfigurationException(
                $"The informer of the subscriptions of {_subscription.Scope} stopped: it failed with {exception.GetType().Name}.",
                _subscription.Scope.ResponsibilityKey.ToString(),
                null,
                null,
                false,
                exception);
            isBroken = true;
        }
        finally
        {
            End(endFailure, isBroken);
        }
    }

    private static PollingSubscriptionSource CreateSource(SubscriptionSettings subscription, StorytellerSettings settings, IAnnotationPages pages, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(settings);

        // The informer's own category, as for ISubscriptionCatalog.WatchExecutionsAsync.
        var logger = new SwitchableLoggerFactory(settings.LoggerFactory ?? loggerFactory).CreateLogger(SubscriptionCatalog.InformerLogCategory);
        return new PollingSubscriptionSource(pages, subscription.PollInterval, subscription.MaxConcurrentReads, settings.TimeProvider, settings.NextRandom, logger);
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private static StorytellerConfigurationException Copy(StorytellerConfigurationException failure)
    {
        return new StorytellerConfigurationException(failure.Message, failure.Key, failure.StatusCode, failure.ErrorCode, failure.IsTransient, failure);
    }

    private async Task ApplyAsync(SubscriptionBatch batch, CancellationToken stoppingToken)
    {
        if (batch.Failure is { } failure)
        {
            // The source logged it; what is held stays, documents included.
            _lastPollError = failure;
            _consecutivePollFailures++;
            Publish([]);
            return;
        }

        var removed = ApplyChanges(batch.Changes, out var annotationsChanged);
        var reads = await ReadDocumentsAsync(batch.DirtyKeys, stoppingToken).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow();
        var changed = new List<StorytellerSubscriptionChange>();
        var added = new List<StorytellerSubscriptionChange>();

        foreach (var (key, tracked) in _tracked.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var hasNewAnnotations = annotationsChanged.Contains(key);
            var hasNewData = false;
            var isRead = reads.TryGetValue(key, out var read);

            if (isRead && read!.Entry is { } entry)
            {
                // A 304 shares the held data, and so does a new tag on the same document; a new version that changed
                // only labels or values reads to equal data. A secret that rotated on a 304 does not.
                hasNewData = tracked.Entry is { } held
                    && !ReferenceEquals(held.Data, entry.Data)
                    && !DocumentEntry.HasSameData(held.Data, entry.Data);

                // The newest good entry, so its tag and version stay current.
                tracked.Entry = entry;
                tracked.LastSucceededAt = now;
                tracked.LastError = null;
                tracked.LastErrorAt = null;
            }
            else if (isRead)
            {
                // Static stability: a failure keeps the last good document, whatever its class.
                tracked.LastError = read!.Failure;
                tracked.LastErrorAt = now;
                LogFailedRead(tracked, read.Failure!);
            }

            // Pending, or nothing new to hand out.
            if (tracked.Entry is null || (!isRead && !hasNewAnnotations && tracked.Published is not null))
            {
                continue;
            }

            var wasPublished = tracked.Published is not null;
            var subscription = tracked.NewSnapshot();

            if (!wasPublished)
            {
                added.Add(new StorytellerSubscriptionChange(StorytellerChangeKind.Added, subscription));
            }
            else if (hasNewAnnotations || hasNewData)
            {
                changed.Add(new StorytellerSubscriptionChange(StorytellerChangeKind.Changed, subscription));
            }
        }

        _isReady = true;
        _lastPollSucceededAt = now;
        _lastPollError = null;
        _consecutivePollFailures = 0;

        if (removed.Count + changed.Count + added.Count > 0)
        {
            _logger.LogInformation(
                "The subscriptions of {Scope}: {Removed} removed, {Changed} changed, {Added} added; {Count} held, {Pending} pending.",
                _subscription.Scope.ToString(),
                removed.Count,
                changed.Count,
                added.Count,
                _tracked.Values.Count(item => item.Entry is not null),
                _tracked.Values.Count(item => item.Entry is null));
        }

        Publish([.. removed, .. changed, .. added]);
    }

    // The source's changes, Removed first, then Changed, then Added, each by key. Returns the Removed events of the
    // subscriptions that were published, and the keys whose execution or usage changed.
    private List<StorytellerSubscriptionChange> ApplyChanges(IReadOnlyList<StorytellerExecutionChange> changes, out HashSet<string> annotationsChanged)
    {
        var removed = new List<StorytellerSubscriptionChange>();
        annotationsChanged = new HashSet<string>(StringComparer.Ordinal);

        foreach (var change in changes)
        {
            var key = change.Execution.Key;

            switch (change.Kind)
            {
                case StorytellerChangeKind.Removed:
                    if (_tracked.Remove(key, out var gone))
                    {
                        // Its resolved secrets go with it.
                        _documents.Evict(gone.Key, _subscription.Mode);

                        // A pending one was never Added, so it is not Removed either.
                        if (gone.Published is { } published)
                        {
                            removed.Add(new StorytellerSubscriptionChange(StorytellerChangeKind.Removed, published, change.Removal));
                        }
                    }

                    break;

                case StorytellerChangeKind.Changed when _tracked.TryGetValue(key, out var held):
                    held.Execution = change.Execution;
                    held.Usage = change.Usage;
                    annotationsChanged.Add(key);
                    break;

                case StorytellerChangeKind.Added when _tracked.TryGetValue(key, out var known):
                    // Not expected of a source; taken as a change of what is held.
                    known.Execution = change.Execution;
                    known.Usage = change.Usage;
                    annotationsChanged.Add(key);
                    break;

                case StorytellerChangeKind.Added:
                    _tracked[key] = new Tracked(_subscription.Scope.ExecutionKey(change.Execution.SubjectName), change.Execution, change.Usage);
                    break;
            }
        }

        return removed;
    }

    // The documents of the dirty keys that are held, at most MaxConcurrentReads at once. Failed reads are answers too,
    // so one failing tenant leaves the others alone; a stop cancels the waits.
    private async Task<Dictionary<string, DocumentRead>> ReadDocumentsAsync(IReadOnlyCollection<string> dirtyKeys, CancellationToken stoppingToken)
    {
        var targets = dirtyKeys
            .Distinct(StringComparer.Ordinal)
            .Where(_tracked.ContainsKey)
            .Select(key => _tracked[key])
            .ToList();

        using var limiter = new SemaphoreSlim(_subscription.MaxConcurrentReads, _subscription.MaxConcurrentReads);
        var reads = targets.Select(tracked => (tracked.KeyText, Read: ReadDocumentAsync(tracked.Key, limiter, stoppingToken))).ToList();

        // Every read ends before a stop is thrown, so none uses the limiter after it is disposed.
        await Task.WhenAll(reads.Select(item => item.Read)).ConfigureAwait(false);

        var results = new Dictionary<string, DocumentRead>(StringComparer.Ordinal);

        foreach (var (key, read) in reads)
        {
            results[key] = await read.ConfigureAwait(false);
        }

        return results;
    }

    private async Task<DocumentRead> ReadDocumentAsync(FullKey key, SemaphoreSlim limiter, CancellationToken stoppingToken)
    {
        await limiter.WaitAsync(stoppingToken).ConfigureAwait(false);

        try
        {
            return new DocumentRead(await _documents.GetEntryAsync(key, _subscription.Mode, stoppingToken).ConfigureAwait(false), null);
        }
        catch (StorytellerConfigurationException exception)
        {
            return new DocumentRead(null, exception);
        }
        catch (Exception exception) when (exception is not ObjectDisposedException && !stoppingToken.IsCancellationRequested)
        {
            // Unknown, as from a resolver of the application: no verdict of the platform. Its message stays in the inner
            // exception; it is not ours to vouch for.
            return new DocumentRead(
                null,
                new StorytellerConfigurationException($"Reading the configuration {key} failed with {exception.GetType().Name}.", key.ToString(), null, null, true, exception));
        }
        finally
        {
            limiter.Release();
        }
    }

    private void LogFailedRead(Tracked tracked, StorytellerConfigurationException failure)
    {
        _logger.LogWarning(
            tracked.Entry is null
                ? "Reading the configuration of the subscription {Key} failed ({Class}); it stays pending, without an event, and the next poll reads it again. {Error}"
                : "Reading the configuration of the subscription {Key} failed ({Class}); it keeps its last good document, and the next poll reads it again. {Error}",
            tracked.KeyText,
            failure.IsTransient ? "transient" : "definitive",
            failure.Message);
    }

    // A new view and status, then the events, in order. Under the lock: the named options drop what the changes made
    // stale, then the watchers and the waiters hear of them, so whoever saw a change and asks for its options gets them
    // bound to it. After the lock: the named options notify.
    private void Publish(IReadOnlyList<StorytellerSubscriptionChange> changes)
    {
        var all = _tracked.Values
            .Select(tracked => tracked.Published)
            .OfType<StorytellerSubscription>()
            .OrderBy(subscription => subscription.Subject, StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
        var view = new View(all, all.ToDictionary(subscription => subscription.Subject, StringComparer.Ordinal).AsReadOnly());
        var status = new StorytellerSubscriptionsStatus
        {
            IsReady = _isReady,
            LastPollSucceededAt = _lastPollSucceededAt,
            LastPollError = _lastPollError,
            ConsecutivePollFailures = _consecutivePollFailures,
            Subscriptions = all.Count,
            Pending = _tracked.Values
                .Where(tracked => tracked.Entry is null)
                .OrderBy(tracked => tracked.KeyText, StringComparer.Ordinal)
                .Select(tracked => new StorytellerPendingSubscription(tracked.KeyText, tracked.LastError))
                .ToList()
                .AsReadOnly(),
            Failing = _tracked.Values.Count(tracked => tracked.Entry is not null && tracked.LastError is not null),
        };

        Listener[] listeners;

        lock (_lock)
        {
            Volatile.Write(ref _view, view);
            Volatile.Write(ref _status, status);
            listeners = [.. _listeners];

            foreach (var change in changes)
            {
                foreach (var listener in listeners)
                {
                    Notify(listener.Invalidate, change);
                }
            }

            foreach (var change in changes)
            {
                foreach (var watcher in _watchers)
                {
                    watcher.Writer.TryWrite(change);
                }
            }

            Signal();
        }

        foreach (var change in changes)
        {
            foreach (var listener in listeners)
            {
                Notify(listener.Notify, change);
            }
        }
    }

    // A listener that throws must not stop the loop or the other listeners.
    private void Notify(Action<StorytellerSubscriptionChange> listener, StorytellerSubscriptionChange change)
    {
        try
        {
            listener(change);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                "A listener of the subscriptions failed on {Kind} of {Key} with {Type}.",
                change.Kind,
                change.Subscription.Execution.Key,
                exception.GetType().Name);
        }
    }

    // The first end wins: a stop, the one poll of a zero interval, or a loop that broke. The watchers end, and the
    // waiters that are not ready get the failure, or a cancellation. isBroken: the loop broke; the status reports its
    // failure as that of the latest poll, so it is degraded, and the watchers end with it, so they cannot take it for
    // a stop. What is held stays, as for a failed poll.
    private void End(StorytellerConfigurationException? failure, bool isBroken = false)
    {
        lock (_lock)
        {
            if (_hasEnded)
            {
                return;
            }

            _hasEnded = true;
            _endFailure = failure;

            if (isBroken && failure is not null)
            {
                _watchFailure = failure;
                Volatile.Write(ref _status, _status with
                {
                    LastPollError = failure,
                    ConsecutivePollFailures = _status.ConsecutivePollFailures + 1,
                });
            }

            // A copy per watcher, as for the waiters.
            foreach (var watcher in _watchers)
            {
                watcher.Writer.TryComplete(_watchFailure is { } watchFailure ? Copy(watchFailure) : null);
            }

            _watchers.Clear();
            Signal();
        }
    }

    // Under the lock.
    private void Signal()
    {
        var progress = _progress;
        _progress = NewSignal();
        progress.TrySetResult();
    }

    private void StopListening(Listener listener)
    {
        lock (_lock)
        {
            _listeners.Remove(listener);
        }
    }

    // What GetAll and Find read: the published subscriptions, by subject.
    private sealed record View(IReadOnlyList<StorytellerSubscription> All, IReadOnlyDictionary<string, StorytellerSubscription> BySubject)
    {
        public static readonly View Empty = new(
            Array.AsReadOnly(Array.Empty<StorytellerSubscription>()),
            new Dictionary<string, StorytellerSubscription>(StringComparer.Ordinal).AsReadOnly());
    }

    // A read of a document: the entry, or the failure.
    private sealed record DocumentRead(DocumentEntry? Entry, StorytellerConfigurationException? Failure);

    // A subscription the source reported: its annotations, its last good entry and the state of its reads. Published:
    // what GetAll hands out; null while it is pending.
    private sealed class Tracked
    {
        public Tracked(FullKey key, StorytellerExecution execution, StorytellerAnnotation usage)
        {
            Key = key;
            KeyText = key.ToString();
            Execution = execution;
            Usage = usage;
        }

        public FullKey Key { get; }

        public string KeyText { get; }

        public StorytellerExecution Execution { get; set; }

        public StorytellerAnnotation Usage { get; set; }

        public DocumentEntry? Entry { get; set; }

        public DateTimeOffset? LastSucceededAt { get; set; }

        public StorytellerConfigurationException? LastError { get; set; }

        public DateTimeOffset? LastErrorAt { get; set; }

        public StorytellerSubscription? Published { get; private set; }

        // A new snapshot of what is held; only with an entry.
        public StorytellerSubscription NewSnapshot()
        {
            Published = new StorytellerSubscription(Entry!)
            {
                Execution = Execution,
                Usage = Usage,
                LastError = LastError,
                LastErrorAt = LastErrorAt,
                LastSucceededAt = LastSucceededAt!.Value,
            };

            return Published;
        }
    }

    // One registration of Listen; a class, so two with the same delegates stay apart.
    private sealed class Listener : IDisposable
    {
        private readonly SubscriptionsService _owner;

        public Listener(SubscriptionsService owner, Action<StorytellerSubscriptionChange> invalidate, Action<StorytellerSubscriptionChange> notify)
        {
            _owner = owner;
            Invalidate = invalidate;
            Notify = notify;
        }

        public Action<StorytellerSubscriptionChange> Invalidate { get; }

        public Action<StorytellerSubscriptionChange> Notify { get; }

        public void Dispose()
        {
            _owner.StopListening(this);
        }
    }
}
