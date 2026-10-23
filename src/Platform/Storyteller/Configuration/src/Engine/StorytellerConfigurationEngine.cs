using _42.Platform.Storyteller.Sdk.Configuring.Caching;
using _42.Platform.Storyteller.Sdk.Configuring.Logging;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Configuring.References;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Configuring.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.Engine;

// The state of one Storyteller source, shared by every provider its builders build: the transport, the snapshot, the
// timer and the status. ConfigurationManager builds new providers whenever its sources change, so the state cannot
// live in a provider; one source has one poller and one cache writer, however often it is built.
//
// A run (the startup, a timer refresh, TryRefreshAsync or a switch) reads, applies and swaps under the run gate (steps
// 1-4) and releases it. Steps 5-6 run on the notification lane, one pass at a time: the notifier publishes the current
// snapshot to every provider and reloads each, then confirms and caches it, or rolls it back. Passes never overlap, so a
// reload error belongs to the snapshot of its pass. A run started from a change callback does not wait for the lane, so
// it never waits on its own notification: the notifier that holds the lane notifies its snapshot after the pass.
//
// Limitation: a change raised outside the provider (IConfigurationRoot.Reload() by the application, or another
// provider's reload of the same root) can still coalesce with a notification: ChangeToken.OnChange delivers a change raised
// while its callback runs once that callback returns, on that callback's thread. The rebind error of a document may then
// surface in that other reload instead of in the pass, and the pass confirms the document.
internal sealed class StorytellerConfigurationEngine : IStorytellerConfigurationRefresher
{
    // The category an application filters on; the provider is the public face of the engine.
    private const string LogCategory = "_42.Platform.Storyteller.Sdk.Configuring.StorytellerConfigurationProvider";

    // The longest due time a system timer takes: 0xFFFFFFFE ms, about 49.7 days.
    private static readonly TimeSpan MaxTimerDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    // Set around the reloads of a pass, of any engine. A run started under it (from a change callback, also through a
    // task the callback started) does not wait for a lane.
    private static readonly AsyncLocal<bool> InNotification = new();

    private readonly StorytellerSettings _settings;
    private readonly SwitchableLoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private readonly LastKnownGoodCache? _cache;
    private readonly ReferenceSubstitution _substitution;
    private readonly DocumentIdentity _configured;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _lane = new(1, 1);
    private readonly Lock _lock = new();
    private readonly Lock _cacheWrite = new();
    private readonly List<StorytellerConfigurationProvider> _providers = [];

    // Guarded by _cacheWrite: per cache identity, the sequence of the newest snapshot whose answer the cache followed.
    // The lane writes in the order of the snapshots, but a 304 deletes under the run gate, so a pass must not write an
    // answer that a later 304 said not to store.
    private readonly Dictionary<string, long> _cacheDecisions = new(StringComparer.Ordinal);

    // Guarded by _lock; _snapshot is also read without it.
    private ConfigurationSnapshot _snapshot;
    private long _sequence;
    private EngineLifetime? _lifetime;
    private Task? _startup;
    private bool _isLoaded;
    private QueuedRefresh? _queuedRefresh;
    private Task _timerRun = Task.CompletedTask;
    private DateTimeOffset? _lastAppliedAt;
    private DateTimeOffset? _lastSucceededAt;
    private StorytellerConfigurationException? _lastError;
    private DateTimeOffset? _lastErrorAt;
    private int _consecutiveFailures;
    private long _totalFailures;

    // Guarded by _lock: the snapshot of the last pass in which every provider reloaded without a throw, so the target of
    // a rollback.
    private ConfigurationSnapshot _confirmed;

    // Guarded by _lock: the sequence of the last snapshot a pass took, or that the providers loaded.
    private long _notifiedSequence;

    // Under the run gate: computed by the first run that needs the cache.
    private string? _fingerprint;

    public StorytellerConfigurationEngine(StorytellerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;
        _loggerFactory = new SwitchableLoggerFactory(settings.LoggerFactory);
        _logger = _loggerFactory.CreateLogger(LogCategory);
        _cache = settings.CacheDirectory is { } directory
            ? new LastKnownGoodCache(directory, settings.FileSystem, settings.CacheProtector, settings.TimeProvider, _loggerFactory.CreateLogger(LogCategory))
            : null;
        _substitution = new ReferenceSubstitution(settings.ReferenceResolver, settings.Mode, settings.SecretRefreshInterval, settings.TimeProvider);
        _configured = DocumentIdentity.Create(
            settings.Endpoint,
            settings.Key ?? throw new ArgumentException("The provider needs a key.", nameof(settings)),
            settings.Mode);
        _snapshot = ConfigurationSnapshot.Empty(_configured);
        _confirmed = _snapshot;
    }

    private enum RunState
    {
        Succeeded = 0,
        Failed,
        NotLoaded,
        Stopped,
    }

    public string ActiveKey => Snapshot.Identity.Key;

    public StorytellerConfigurationStatus Status
    {
        get
        {
            lock (_lock)
            {
                var snapshot = _snapshot;

                return new StorytellerConfigurationStatus
                {
                    ActiveKey = snapshot.Identity.Key,
                    ETag = snapshot.ETag,
                    Origin = snapshot.Origin,
                    LastAppliedAt = _lastAppliedAt,
                    LastSucceededAt = _lastSucceededAt,
                    LastError = _lastError,
                    LastErrorAt = _lastErrorAt,
                    ConsecutiveFailures = _consecutiveFailures,
                    TotalFailures = _totalFailures,
                    IsDegraded = snapshot.Origin != StorytellerConfigurationOrigin.Platform
                        || _consecutiveFailures > 0
                        || snapshot.Rejection is not null
                        || snapshot.RejectedData is not null,
                };
            }
        }
    }

    internal StorytellerSettings Settings => _settings;

    internal ConfigurationSnapshot Snapshot => Volatile.Read(ref _snapshot);

    // The run the timer started last; tests await it after advancing their clock.
    internal Task TimerRun
    {
        get
        {
            lock (_lock)
            {
                return _timerRun;
            }
        }
    }

    // Whether a provider is attached, so the engine polls.
    internal bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _lifetime is not null;
            }
        }
    }

    public Task<bool> TryRefreshAsync(CancellationToken cancellationToken = default)
    {
        QueuedRefresh queued;
        var isNew = false;
        var isNested = InNotification.Value;

        lock (_lock)
        {
            if (!_isLoaded || _lifetime is null)
            {
                return Task.FromResult(false);
            }

            // Callers share a run that has not taken the gate yet; one that has may have sent its request before them.
            if (_queuedRefresh is null || _queuedRefresh.Lifetime != _lifetime)
            {
                _lifetime.Runs++;
                _queuedRefresh = new QueuedRefresh(_lifetime);
                isNew = true;
            }

            queued = _queuedRefresh;

            // A caller inside a notification waits for the shared run, so that run must not wait for the lane.
            queued.IsNested |= isNested;
        }

        if (isNew)
        {
            // The run belongs to every caller, so no caller's token cancels it.
            _ = Task.Run(() => RunQueuedAsync(queued), CancellationToken.None);
        }

        return queued.Completion.Task.WaitAsync(cancellationToken);
    }

    public async Task SwitchKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var target = SwitchTarget.Parse(key, _configured);
        var isNested = InNotification.Value;
        EngineLifetime lifetime;
        Task? startup;

        lock (_lock)
        {
            startup = _isLoaded ? null : _startup ?? throw NotLoadedException();
            lifetime = _lifetime ?? throw StoppedException();
            lifetime.Runs++;
        }

        try
        {
            if (startup is not null)
            {
                try
                {
                    await startup.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // The failure of the startup is reported by its Load(); the run below finds nothing loaded.
                }
            }

            var result = await RunAsync(lifetime, target, cancellationToken, null, isNested).ConfigureAwait(false);

            switch (result.State)
            {
                case RunState.Failed:
                    throw result.Error!;

                case RunState.NotLoaded:
                    throw NotLoadedException();

                case RunState.Stopped:
                    throw StoppedException();
            }
        }
        finally
        {
            EndRun(lifetime);
        }
    }

    // The delay of the next poll: RefreshInterval × (0.9 + 0.2 × r), so a fleet started together drifts apart. Capped at
    // the longest due time of a system timer, which validation keeps out of reach; a throw here would come after a
    // committed startup.
    internal static TimeSpan ComputeDelay(TimeSpan interval, double random)
    {
        var ticks = interval.Ticks * (0.9 + (0.2 * random));
        return ticks < MaxTimerDelay.Ticks ? TimeSpan.FromTicks((long)ticks) : MaxTimerDelay;
    }

    // At the construction of a provider, that is, at Build(). The first attach starts the engine; one after the last
    // detach starts it again, with a new epoch, transport and timer.
    internal void Attach(StorytellerConfigurationProvider provider)
    {
        lock (_lock)
        {
            if (_lifetime is null)
            {
                _lifetime = new EngineLifetime(TransportFactory.Create(_settings), _logger);

                // A restart: no provider is left, and the new ones load the current snapshot, so it is the one every
                // provider took. Set here rather than at their Load(), so a snapshot a run swaps in meanwhile is
                // confirmed only by its own pass.
                if (_isLoaded)
                {
                    _confirmed = _snapshot;
                    _notifiedSequence = _snapshot.Sequence;
                }

                ArmTimer(_lifetime);
            }

            _providers.Add(provider);
        }
    }

    // The last detach, which every host shutdown makes, stops the engine: a new epoch, no timer, the in-flight run
    // cancelled, and the transport disposed once that run ended.
    internal void Detach(StorytellerConfigurationProvider provider)
    {
        EngineLifetime stopped;
        ITimer? timer;

        lock (_lock)
        {
            if (!_providers.Remove(provider) || _providers.Count > 0 || _lifetime is null)
            {
                return;
            }

            stopped = _lifetime;
            _lifetime = null;
            stopped.MarkStopped();
            timer = stopped.Timer;
            stopped.Timer = null;

            // Held like a run, so the transport is not disposed before its runs are cancelled.
            stopped.Runs++;
        }

        timer?.Dispose();
        stopped.CancelRuns();
        EndRun(stopped);
    }

    // The first Load() runs the startup, synchronously; concurrent first loads share it. A failed startup is not
    // remembered, so the next Load() runs it again. Later loads publish the current snapshot without a request:
    // IConfigurationRoot.Reload() does not fetch, the refresher does.
    internal void Load(StorytellerConfigurationProvider provider)
    {
        Task startup;

        lock (_lock)
        {
            startup = _isLoaded ? Task.CompletedTask : _startup ??= Task.Run(StartupAsync);
        }

        // Off any SynchronizationContext, and bounded by ReadTimeout. Not on the lane: the root reads Data after Load(),
        // and Load() may come from a change callback while a pass holds the lane.
        startup.GetAwaiter().GetResult();
        provider.Publish(Snapshot);
    }

    // At the start of the host: the container's factory, for an engine that was given none. A degraded start happened
    // before any logger existed, so it is reported now, and so are the warnings of the applied document: a document that
    // does not change is applied once, at the startup.
    internal void UseLoggerFactory(ILoggerFactory loggerFactory)
    {
        if (_loggerFactory.IsGiven)
        {
            return;
        }

        _loggerFactory.Use(loggerFactory);

        if (Status is { IsDegraded: true } status)
        {
            _logger.LogWarning(
                "The configuration {Key} is degraded: it comes from {Origin}, after {ConsecutiveFailures} consecutive failed reads. {Error}",
                status.ActiveKey,
                status.Origin,
                status.ConsecutiveFailures,
                status.LastError?.Message ?? "No error was recorded.");
        }

        ConfigurationSnapshot snapshot;
        EngineLifetime? lifetime;

        lock (_lock)
        {
            snapshot = _snapshot;
            lifetime = _lifetime;
        }

        lifetime?.Reader.WarnUnboundBindings(snapshot.Identity, snapshot.Document?.Content as JToken);
        _substitution.WarnUnresolved(_logger, snapshot.Identity.Key, snapshot.Substitution);
    }

    // After the host stopped: it disposes its logger factory before the configuration.
    internal void ReleaseLoggerFactory()
    {
        if (!_loggerFactory.IsGiven)
        {
            _loggerFactory.Use(NullLoggerFactory.Instance);
        }
    }

    // The types only: a binding exception may quote the value that did not convert.
    private static string DescribeTypes(Exception exception)
    {
        var types = new List<string>();
        Collect(exception);
        return string.Join(", ", types.Distinct(StringComparer.Ordinal));

        void Collect(Exception current)
        {
            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    Collect(inner);
                }

                return;
            }

            types.Add(current.GetType().Name);
        }
    }

    // Keys compare ordinally: the dictionaries ignore case, but a renamed property must reach GetChildren() and binding.
    private static bool HasSameData(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        var keys = new HashSet<string>(right.Keys, StringComparer.Ordinal);

        return left.All(pair => keys.Contains(pair.Key)
            && right.TryGetValue(pair.Key, out var value)
            && string.Equals(pair.Value, value, StringComparison.Ordinal));
    }

    // A read that was abandoned at ReadTimeout may fail later; nobody waits for it any more.
    private static void Observe(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static StorytellerConfigurationException TimedOut(DocumentIdentity identity, TimeSpan readTimeout)
    {
        return new StorytellerConfigurationException(
            $"Reading {identity.Key} did not finish within the ReadTimeout of {readTimeout}.",
            identity.Key,
            null,
            null,
            true);
    }

    private static InvalidOperationException NotLoadedException()
    {
        return new InvalidOperationException("The Storyteller configuration is not loaded yet; build the configuration first.");
    }

    private static InvalidOperationException StoppedException()
    {
        return new InvalidOperationException("The Storyteller configuration is stopped: every configuration root that used it was disposed.");
    }

    private DateTimeOffset Now()
    {
        return _settings.TimeProvider.GetUtcNow();
    }

    private EngineLifetime? BeginRun()
    {
        lock (_lock)
        {
            if (_lifetime is null)
            {
                return null;
            }

            _lifetime.Runs++;
            return _lifetime;
        }
    }

    private void EndRun(EngineLifetime lifetime)
    {
        bool dispose;

        lock (_lock)
        {
            lifetime.Runs--;
            dispose = lifetime.IsStopped && lifetime.Runs == 0;
        }

        if (dispose)
        {
            lifetime.Dispose();
        }
    }

    // One-shot, re-armed after each run under the epoch check, so runs never overlap and a stopped engine never polls.
    private void ArmTimer(EngineLifetime lifetime)
    {
        if (_settings.RefreshInterval == TimeSpan.Zero)
        {
            return;
        }

        var delay = ComputeDelay(_settings.RefreshInterval, _settings.NextRandom());

        lock (_lock)
        {
            if (_lifetime != lifetime || !_isLoaded)
            {
                return;
            }

            if (lifetime.Timer is { } timer)
            {
                timer.Change(delay, Timeout.InfiniteTimeSpan);
            }
            else if (ExecutionContext.IsFlowSuppressed())
            {
                lifetime.Timer = _settings.TimeProvider.CreateTimer(OnTimer, lifetime, delay, Timeout.InfiniteTimeSpan);
            }
            else
            {
                // The timer outlives whoever armed it first: their AsyncLocals (an Activity, a logging scope) must not
                // flow into every poll.
                using (ExecutionContext.SuppressFlow())
                {
                    lifetime.Timer = _settings.TimeProvider.CreateTimer(OnTimer, lifetime, delay, Timeout.InfiniteTimeSpan);
                }
            }
        }
    }

    private void OnTimer(object? state)
    {
        var lifetime = (EngineLifetime)state!;

        lock (_lock)
        {
            if (_lifetime != lifetime)
            {
                return;
            }

            lifetime.Runs++;

            // Off the timer's thread: a run calls the application's change callbacks.
            _timerRun = Task.Run(() => RunTimerAsync(lifetime));
        }
    }

    private async Task RunTimerAsync(EngineLifetime lifetime)
    {
        try
        {
            await RunAsync(lifetime, null, CancellationToken.None, null, false).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Refreshing the configuration {Key} failed unexpectedly.", ActiveKey);
        }
        finally
        {
            EndRun(lifetime);
        }
    }

    private async Task RunQueuedAsync(QueuedRefresh queued)
    {
        var succeeded = false;

        try
        {
            var result = await RunAsync(queued.Lifetime, null, CancellationToken.None, queued, false).ConfigureAwait(false);
            succeeded = result.State == RunState.Succeeded;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Refreshing the configuration {Key} failed unexpectedly.", ActiveKey);
        }
        finally
        {
            lock (_lock)
            {
                if (_queuedRefresh == queued)
                {
                    _queuedRefresh = null;
                }
            }

            EndRun(queued.Lifetime);

            // After step 6: an awaited TryRefreshAsync returns once IConfiguration shows the result.
            queued.Completion.TrySetResult(succeeded);
        }
    }

    // The startup read: steps 1-4 and 6. Nothing is notified: Load() publishes to the provider that called it, and the
    // root reads Data after Load().
    private async Task StartupAsync()
    {
        try
        {
            var lifetime = BeginRun() ?? throw StoppedException();

            try
            {
                await RunStartupAsync(lifetime).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && lifetime.IsStopped)
            {
                throw StoppedException();
            }
            finally
            {
                EndRun(lifetime);
            }
        }
        finally
        {
            lock (_lock)
            {
                _startup = null;
            }
        }
    }

    private async Task RunStartupAsync(EngineLifetime lifetime)
    {
        await _gate.WaitAsync(lifetime.Stopping).ConfigureAwait(false);

        var identity = _configured;
        ConfigurationSnapshot started;
        StorytellerConfigurationException? degradation = null;

        try
        {
            ConfigurationSnapshot? draft = null;
            StorytellerConfigurationException? failure = null;
            var isReadFailure = false;
            CacheEntry? entry = null;

            using (var scope = new RunScope(_settings.ReadTimeout, lifetime.Stopping, CancellationToken.None))
            {
                try
                {
                    var cacheIdentity = await GetCacheIdentityAsync(identity, scope.Token).ConfigureAwait(false);
                    entry = cacheIdentity is null ? null : _cache!.Read(cacheIdentity);

                    // Only a protected entry's tag is sent: a plain entry is not authenticated.
                    switch (await ReadAsync(lifetime, scope, identity, entry?.TrustedETag).ConfigureAwait(false))
                    {
                        case ReadOutcome.Changed changed:
                            MarkAnswered(lifetime);
                            draft = await CreateSnapshotAsync(identity, changed.Document, changed.ETag, changed.CacheControl, StorytellerConfigurationOrigin.Platform, scope).ConfigureAwait(false);
                            draft = draft with { WritesCache = true };
                            break;

                        case ReadOutcome.NotModified notModified when entry?.TrustedETag is not null:
                            MarkAnswered(lifetime);
                            _cache!.Revalidated(cacheIdentity!, notModified.CacheControl);
                            draft = await CreateSnapshotAsync(identity, entry.Document, notModified.ETag, notModified.CacheControl, StorytellerConfigurationOrigin.Platform, scope).ConfigureAwait(false);
                            break;

                        case ReadOutcome.NotModified:
                            failure = UnexpectedNotModified(identity);
                            isReadFailure = true;
                            break;

                        case ReadOutcome.Failure readFailure:
                            failure = readFailure.ToException(identity.Key);
                            isReadFailure = true;
                            break;
                    }
                }
                catch (StorytellerConfigurationException exception)
                {
                    // The document did not apply: invalid, or a reference that did not resolve.
                    failure = exception;
                }
                catch (OperationCanceledException) when (scope.IsTimedOut)
                {
                    failure = TimedOut(identity, _settings.ReadTimeout);
                    isReadFailure = true;
                }

                if (draft is null)
                {
                    // Optional is only about reachability: a wrong key, a refused credential, withheld secrets or an
                    // invalid document fail an optional provider too. A cache entry stands in for an unreachable
                    // platform, not for a document that does not apply.
                    if (failure!.IsTransient)
                    {
                        // Within what is left of ReadTimeout, which bounds the whole startup.
                        if (isReadFailure && entry is not null)
                        {
                            draft = await TryUseCacheEntryAsync(identity, entry, scope).ConfigureAwait(false);
                        }

                        if (draft is null && _settings.Optional)
                        {
                            draft = ConfigurationSnapshot.Empty(identity);
                        }
                    }

                    if (draft is null)
                    {
                        Fail(lifetime, failure);
                        throw failure;
                    }

                    degradation = failure;
                }
            }

            lock (_lock)
            {
                if (_lifetime != lifetime)
                {
                    throw StoppedException();
                }

                // Nothing is notified: Load() publishes it to every provider.
                started = Stamp(draft);
                _snapshot = started;
                _confirmed = started;
                _notifiedSequence = started.Sequence;
                _isLoaded = true;

                // An empty start applies no document.
                if (started.Origin != StorytellerConfigurationOrigin.Empty)
                {
                    _lastAppliedAt = Now();
                }

                if (degradation is null)
                {
                    _consecutiveFailures = 0;
                }
                else
                {
                    RecordFailure(degradation);
                }
            }
        }
        finally
        {
            _gate.Release();
        }

        WriteCache(lifetime, started);
        lifetime.Reader.WarnUnboundBindings(identity, started.Document?.Content as JToken);
        _substitution.WarnUnresolved(_logger, identity.Key, started.Substitution);

        switch (started.Origin)
        {
            case StorytellerConfigurationOrigin.Platform:
                _logger.LogInformation("Loaded the configuration {Key}, version {Version}, from the platform.", identity.Key, started.Document?.Version);
                break;

            case StorytellerConfigurationOrigin.Cache:
                _logger.LogWarning(
                    "Started the configuration {Key} from the last-known-good cache, version {Version}; it is refreshed when the platform answers. {Error}",
                    identity.Key,
                    started.Document?.Version,
                    degradation?.Message);
                break;

            default:
                _logger.LogWarning(
                    "Started the optional configuration {Key} empty; it is refreshed when the platform answers. {Error}",
                    identity.Key,
                    degradation?.Message);
                break;
        }

        ArmTimer(lifetime);
    }

    // A transient failure at startup: the entry is usable only if it applies, references included. A resolution that
    // runs out of the startup's ReadTimeout is a transient resolver failure.
    private async Task<ConfigurationSnapshot?> TryUseCacheEntryAsync(DocumentIdentity identity, CacheEntry entry, RunScope scope)
    {
        try
        {
            return await CreateSnapshotAsync(identity, entry.Document, entry.TrustedETag, entry.CacheControl, StorytellerConfigurationOrigin.Cache, scope).ConfigureAwait(false);
        }
        catch (StorytellerConfigurationException exception)
        {
            _logger.LogWarning("The cache entry of the configuration {Key} cannot be applied, so it is not used. {Error}", identity.Key, exception.Message);
            return null;
        }
    }

    // A refresh (key null) or a switch: steps 1-4 under the run gate, then 5-6 on the notification lane. A run started from
    // a change callback (isNested) never waits for the lane.
    private async Task<RunResult> RunAsync(EngineLifetime lifetime, SwitchTarget? target, CancellationToken cancellationToken, QueuedRefresh? queued, bool isNested)
    {
        try
        {
            using (var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Stopping))
            {
                try
                {
                    await _gate.WaitAsync(wait.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsStopped && !cancellationToken.IsCancellationRequested)
                {
                    return new RunResult(RunState.Stopped);
                }
            }

            ConfigurationSnapshot? swapped = null;
            StorytellerConfigurationException? error = null;
            DocumentIdentity identity;

            try
            {
                ConfigurationSnapshot previous;

                lock (_lock)
                {
                    if (queued is not null)
                    {
                        // Started: later callers queue a new run, so whether a caller waits inside a notification is
                        // settled.
                        if (_queuedRefresh == queued)
                        {
                            _queuedRefresh = null;
                        }

                        isNested = queued.IsNested;
                    }

                    if (_lifetime != lifetime)
                    {
                        return new RunResult(RunState.Stopped);
                    }

                    if (!_isLoaded)
                    {
                        return new RunResult(RunState.NotLoaded);
                    }

                    previous = _snapshot;
                }

                identity = target?.Resolve(previous.Identity, _settings.Endpoint) ?? previous.Identity;
                Draft? draft = null;

                using (var scope = new RunScope(_settings.ReadTimeout, lifetime.Stopping, cancellationToken))
                {
                    try
                    {
                        draft = await ReadAndApplyAsync(lifetime, scope, previous, identity).ConfigureAwait(false);
                    }
                    catch (StorytellerConfigurationException exception)
                    {
                        error = exception;
                    }
                    catch (OperationCanceledException) when (scope.IsTimedOut)
                    {
                        error = TimedOut(identity, _settings.ReadTimeout);
                    }
                }

                if (draft is not null)
                {
                    // The caller's token is honoured until the swap.
                    cancellationToken.ThrowIfCancellationRequested();

                    lock (_lock)
                    {
                        if (_lifetime != lifetime)
                        {
                            return new RunResult(RunState.Stopped);
                        }

                        // A draft built on the previous snapshot loses to a rollback that swapped in meanwhile.
                        if (!draft.RequireCurrent || ReferenceEquals(_snapshot, previous))
                        {
                            swapped = Stamp(draft.Snapshot);
                            _snapshot = swapped;
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException
                && lifetime.IsStopped
                && !cancellationToken.IsCancellationRequested)
            {
                return new RunResult(RunState.Stopped);
            }
            finally
            {
                _gate.Release();
            }

            if (error is not null)
            {
                Fail(lifetime, error);
                _logger.LogWarning(
                    "Reading the configuration {Key} failed ({Class}); the configuration {ActiveKey} stays applied. {Error}",
                    identity.Key,
                    error.IsTransient ? "transient" : "definitive",
                    ActiveKey,
                    error.Message);
                return new RunResult(RunState.Failed, error);
            }

            // Its own snapshot was notified and did not bind: the pass recorded and logged the rejection. A snapshot that
            // a newer one replaced before its pass succeeds.
            if (swapped is not null && await NotifyAsync(isNested).ConfigureAwait(false) && GetRejection(swapped) is { } rejection)
            {
                return new RunResult(RunState.Failed, rejection);
            }

            Succeed(lifetime);
            return new RunResult(RunState.Succeeded);
        }
        finally
        {
            ArmTimer(lifetime);
        }
    }

    // Steps 5-6 after a swap. Returns whether this run held the lane until the providers were on the current snapshot, so
    // that the outcome of its own snapshot is known. A nested run takes the lane only if it is free: a notifier that holds
    // it may be the one whose change callback waits for this run, and it notifies the new snapshot after its pass.
    private async ValueTask<bool> NotifyAsync(bool isNested)
    {
        if (isNested)
        {
            if (!_lane.Wait(0))
            {
                return false;
            }
        }
        else
        {
            await _lane.WaitAsync().ConfigureAwait(false);
        }

        Pump();
        return true;
    }

    // Holds the lane: one pass after another until the providers are on the current snapshot. A nested run that swapped
    // meanwhile may have found the lane taken and left its snapshot to this notifier, so after the release the lane is
    // taken again if anything is left.
    private void Pump()
    {
        do
        {
            try
            {
                while (NextPass() is { } pass)
                {
                    Notify(pass);
                }
            }
            finally
            {
                _lane.Release();
            }
        }
        while (IsPending() && _lane.Wait(0));
    }

    private Pass? NextPass()
    {
        lock (_lock)
        {
            if (_lifetime is not { } lifetime || _snapshot.Sequence <= _notifiedSequence)
            {
                return null;
            }

            _notifiedSequence = _snapshot.Sequence;
            return new Pass(lifetime, _snapshot, [.. _providers]);
        }
    }

    private bool IsPending()
    {
        lock (_lock)
        {
            return _lifetime is not null && _snapshot.Sequence > _notifiedSequence;
        }
    }

    private StorytellerConfigurationException? GetRejection(ConfigurationSnapshot snapshot)
    {
        lock (_lock)
        {
            return snapshot.Notification.Rejection;
        }
    }

    // Under _lock: the next sequence, and an outcome of its own.
    private ConfigurationSnapshot Stamp(ConfigurationSnapshot draft)
    {
        return draft with { Sequence = ++_sequence, Notification = new ConfigurationSnapshot.NotificationOutcome() };
    }

    // Steps 1-3 of a refresh or a switch: the draft to swap in, or null when nothing changed. Failures throw
    // StorytellerConfigurationException with their class.
    private async Task<Draft?> ReadAndApplyAsync(EngineLifetime lifetime, RunScope scope, ConfigurationSnapshot previous, DocumentIdentity identity)
    {
        var isSwitch = identity != previous.Identity;
        CacheIdentity? cacheIdentity = null;
        CacheEntry? entry = null;
        string? etag;

        if (isSwitch)
        {
            // The new identity's protected entry; a plain entry's tag is never sent.
            cacheIdentity = await GetCacheIdentityAsync(identity, scope.Token).ConfigureAwait(false);
            entry = cacheIdentity is null ? null : _cache!.Read(cacheIdentity);
            etag = entry?.TrustedETag;
        }
        else
        {
            etag = previous.ETag;
        }

        switch (await ReadAsync(lifetime, scope, identity, etag).ConfigureAwait(false))
        {
            case ReadOutcome.Changed changed:
            {
                MarkAnswered(lifetime);
                var snapshot = await CreateSnapshotAsync(identity, changed.Document, changed.ETag, changed.CacheControl, StorytellerConfigurationOrigin.Platform, scope).ConfigureAwait(false);
                snapshot = snapshot with { WritesCache = true };

                // A new tag with the same keys and values, as after an unconditional poll: nothing to reload.
                if (!isSwitch && HasSameData(snapshot.Data, previous.Data))
                {
                    snapshot = snapshot with { Data = previous.Data };
                }

                // The fingerprint, for the pass that writes the entry.
                await GetCacheIdentityAsync(identity, scope.Token).ConfigureAwait(false);
                return new Draft(snapshot, false);
            }

            // A switch sent the tag of a protected entry, which the platform confirmed.
            case ReadOutcome.NotModified notModified when isSwitch && etag is not null:
            {
                MarkAnswered(lifetime);
                _cache!.Revalidated(cacheIdentity!, notModified.CacheControl);
                var snapshot = await CreateSnapshotAsync(identity, entry!.Document, notModified.ETag, notModified.CacheControl, StorytellerConfigurationOrigin.Platform, scope).ConfigureAwait(false);
                return new Draft(snapshot, false);
            }

            // The tag of the snapshot: its document, or the rejected one after a rollback.
            case ReadOutcome.NotModified notModified when etag is not null:
                MarkAnswered(lifetime);
                return await RevalidateAsync(previous, notModified, scope).ConfigureAwait(false);

            case ReadOutcome.NotModified:
                throw UnexpectedNotModified(identity);

            case ReadOutcome.Failure failure:
                throw failure.ToException(identity.Key);

            default:
                throw new InvalidOperationException($"Unknown read outcome {identity}.");
        }
    }

    // Step 2: a 304 of the active identity. The data stays; a draft that changes only metadata shares it, so providers
    // do not reload.
    private async Task<Draft?> RevalidateAsync(ConfigurationSnapshot previous, ReadOutcome.NotModified notModified, RunScope scope)
    {
        // no-store, or no Cache-Control in Resolved mode, without a protector: the identity's plain entry goes, and the
        // pass of an older 200 that is still waiting for the lane must not write it again. A draft below keeps the
        // WritesCache of the snapshot it revalidates, so a 200 it replaces before its pass is still written, under the
        // Cache-Control of this answer.
        if (await GetCacheIdentityAsync(previous.Identity, scope.Token).ConfigureAwait(false) is { } cacheIdentity)
        {
            lock (_cacheWrite)
            {
                if (_cache!.Revalidated(cacheIdentity, notModified.CacheControl))
                {
                    _cacheDecisions[cacheIdentity.Text] = Math.Max(previous.Sequence, _cacheDecisions.GetValueOrDefault(cacheIdentity.Text));
                }
            }
        }

        // A 304 that answers a rejected tag confirms the rejected document, not the restored one, so the origin stays. One
        // that confirms a protected cache entry turns the origin to Platform.
        var origin = previous.Rejection is null ? StorytellerConfigurationOrigin.Platform : previous.Origin;

        // Secret rotation: references older than SecretRefreshInterval are resolved again, fresh ones are reused, and the
        // document is applied again only if a value changed. A failure keeps the snapshot, and the next poll tries again.
        if (previous.Substitution is { } substitution && _substitution.IsRefreshDue(substitution))
        {
            var (data, refreshed) = await ApplyContentAsync(previous.Identity, previous.Document!, scope).ConfigureAwait(false);

            // The values that did not bind are still the ones resolved: nothing to apply. The snapshot keeps its old
            // resolution time, so the next poll looks again, at the memory cache until those values expire, then at the
            // resolver.
            if (previous.RejectedData is not { } rejectedData || !HasSameData(data, rejectedData))
            {
                var draft = previous with
                {
                    ETag = notModified.ETag,
                    CacheControl = notModified.CacheControl,
                    Origin = origin,
                    Substitution = refreshed,
                    Data = HasSameData(data, previous.Data) ? previous.Data : data,
                    RejectedData = null,
                };

                return new Draft(draft, true);
            }
        }

        if (string.Equals(notModified.ETag, previous.ETag, StringComparison.Ordinal)
            && string.Equals(notModified.CacheControl, previous.CacheControl, StringComparison.Ordinal)
            && origin == previous.Origin)
        {
            return null;
        }

        return new Draft(previous with { ETag = notModified.ETag, CacheControl = notModified.CacheControl, Origin = origin }, true);
    }

    // Step 1. The reader classifies everything but cancellation and a closed transport: ReadTimeout is transient here,
    // and so is a closed transport while the engine runs.
    private async Task<ReadOutcome> ReadAsync(EngineLifetime lifetime, RunScope scope, DocumentIdentity identity, string? etag)
    {
        var read = lifetime.Reader.ReadAsync(identity, etag, scope.Token);

        try
        {
            // A handler that ignores cancellation cannot hold the run past ReadTimeout.
            return await read.WaitAsync(scope.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (scope.IsTimedOut)
        {
            Observe(read);
            return new ReadOutcome.Transient(null, null, TimedOut(identity, _settings.ReadTimeout).Message, exception);
        }
        catch (OperationCanceledException)
        {
            Observe(read);
            throw;
        }
        catch (ObjectDisposedException exception) when (!lifetime.IsStopped)
        {
            return new ReadOutcome.Transient(null, null, $"Reading {identity.Key} failed: its connection was closed.", exception);
        }
    }

    private async Task<ConfigurationSnapshot> CreateSnapshotAsync(
        DocumentIdentity identity,
        Configuration document,
        string? etag,
        string? cacheControl,
        StorytellerConfigurationOrigin origin,
        RunScope scope)
    {
        var (data, substitution) = await ApplyContentAsync(identity, document, scope).ConfigureAwait(false);

        return new ConfigurationSnapshot
        {
            Identity = identity,
            ETag = etag,
            Document = document,
            Substitution = substitution,
            Data = data,
            CacheControl = cacheControl,
            Origin = origin,
        };
    }

    // Step 3, shared with the document service.
    private Task<(IReadOnlyDictionary<string, string> Data, SubstitutionResult? Substitution)> ApplyContentAsync(
        DocumentIdentity identity,
        Configuration document,
        RunScope scope)
    {
        return ContentApplication.ApplyAsync(_substitution, identity, document, scope, _settings.ReadTimeout);
    }

    // Step 5, one pass on the lane: every provider publishes the snapshot and reloads, outside every lock of the engine.
    // Passes never overlap, so what a reload throws (an IOptionsMonitor rebind that failed validation or conversion)
    // belongs to this snapshot. Then step 6, or the rollback.
    private void Notify(Pass pass)
    {
        var snapshot = pass.Snapshot;
        List<Exception>? errors = null;
        var outer = InNotification.Value;
        InNotification.Value = true;

        try
        {
            foreach (var provider in pass.Providers)
            {
                if (!provider.Publish(snapshot))
                {
                    continue;
                }

                try
                {
                    provider.Reload();
                }
                catch (Exception exception)
                {
                    (errors ??= []).Add(exception);
                }
            }
        }
        finally
        {
            InNotification.Value = outer;
        }

        switch (errors)
        {
            case null:
                Confirm(pass.Lifetime, snapshot);
                break;

            case [var single]:
                Reject(pass.Lifetime, snapshot, single);
                break;

            default:
                Reject(pass.Lifetime, snapshot, new AggregateException(errors));
                break;
        }
    }

    // Every provider took the snapshot: it is the target of a later rollback, and step 6 writes its cache entry.
    private void Confirm(EngineLifetime lifetime, ConfigurationSnapshot snapshot)
    {
        ConfigurationSnapshot before;

        lock (_lock)
        {
            if (_lifetime != lifetime)
            {
                return;
            }

            before = _confirmed;
            _confirmed = snapshot;

            // A rollback restores the confirmed data, so it applies nothing new.
            if (!ReferenceEquals(snapshot.Data, before.Data))
            {
                _lastAppliedAt = Now();
            }
        }

        WriteCache(lifetime, snapshot);

        if (!ReferenceEquals(snapshot.Data, before.Data))
        {
            _logger.LogInformation("Applied version {Version} of the configuration {Key}.", snapshot.Document?.Version, snapshot.Identity.Key);

            if (!ReferenceEquals(snapshot.Document, before.Document))
            {
                lifetime.Reader.WarnUnboundBindings(snapshot.Identity, snapshot.Document?.Content as JToken);
                _substitution.WarnUnresolved(_logger, snapshot.Identity.Key, snapshot.Substitution);
            }
        }
    }

    // The document did not bind. The failure is recorded once, for this snapshot. While the current snapshot still carries
    // the rejected document (it, or a newer one that changed only its metadata), a new snapshot with the confirmed
    // content and identity goes in, and the next pass rebuilds the options from it. On the same identity it keeps the
    // newest rejected tag, so polls answer 304 instead of applying and rolling back the document every interval. A
    // rotation, the confirmed document with its secrets resolved again, keeps the rejected data instead, so a 304 that
    // resolves the same values applies nothing.
    private void Reject(EngineLifetime lifetime, ConfigurationSnapshot rejected, Exception exception)
    {
        var key = rejected.Identity.Key;
        var types = DescribeTypes(exception);
        StorytellerConfigurationException? error = null;

        lock (_lock)
        {
            if (_lifetime != lifetime)
            {
                return;
            }

            // The restored document did not bind either, when the error stays null: there is nothing older to go back to.
            if (!ReferenceEquals(rejected.Data, _confirmed.Data) || rejected.Identity != _confirmed.Identity)
            {
                var isSameIdentity = rejected.Identity == _confirmed.Identity;
                var isRotation = isSameIdentity && ReferenceEquals(rejected.Document, _confirmed.Document);

                // The types only, and not the exception: a binding exception may quote the value that did not convert,
                // and this error goes to the status, to logs and to the caller of a switch.
                error = new StorytellerConfigurationException(
                    isRotation
                        ? $"The configuration {key} did not bind with its secret references resolved again: a reload callback threw {types}. The previous values are restored; the secrets are tried again when their values change."
                        : $"The configuration {key} did not bind: a reload callback threw {types}. The previous document is restored; the platform's document is tried again when it changes.",
                    key,
                    null,
                    null,
                    false);
                rejected.Notification.Rejection = error;
                RecordFailure(error);
                var current = _snapshot;

                if (ReferenceEquals(current, rejected) || (ReferenceEquals(current.Data, rejected.Data) && current.Identity == rejected.Identity))
                {
                    current.Notification.Rejection ??= error;
                    var target = _confirmed with { WritesCache = false };
                    _snapshot = Stamp(!isSameIdentity
                        ? target
                        : isRotation
                            ? target with { ETag = current.ETag, CacheControl = current.CacheControl, RejectedData = rejected.Data }
                            : target with { ETag = current.ETag, CacheControl = current.CacheControl, Rejection = error });
                }
            }
        }

        if (error is null)
        {
            _logger.LogError("The restored configuration {Key} did not bind either: a reload callback threw {Types}. It stays applied.", key, types);
        }
        else
        {
            _logger.LogWarning("The configuration {Key} did not bind; the configuration {ActiveKey} stays applied. {Error}", key, ActiveKey, error.Message);
        }
    }

    // Step 6, on the lane or at the startup: the answer of a 200, once every provider took it. Serialized with the
    // deletions of 304s, which come under the run gate: an answer that a later 304 said not to store is not written.
    private void WriteCache(EngineLifetime lifetime, ConfigurationSnapshot snapshot)
    {
        if (_cache is null || !snapshot.WritesCache || snapshot.Document is not { } document || _fingerprint is not { } fingerprint)
        {
            return;
        }

        var identity = new CacheIdentity(snapshot.Identity, fingerprint);

        lock (_cacheWrite)
        {
            if (_cacheDecisions.TryGetValue(identity.Text, out var decided) && snapshot.Sequence <= decided)
            {
                return;
            }

            lock (_lock)
            {
                if (_lifetime != lifetime)
                {
                    return;
                }
            }

            _cache.Save(identity, document, snapshot.ETag, snapshot.CacheControl);
            _cacheDecisions[identity.Text] = snapshot.Sequence;
        }
    }

    // Under the run gate: the credential is immutable, so its fingerprint is computed once.
    private async ValueTask<CacheIdentity?> GetCacheIdentityAsync(DocumentIdentity identity, CancellationToken cancellationToken)
    {
        if (_cache is null)
        {
            return null;
        }

        _fingerprint ??= await CredentialFingerprint.ComputeAsync(_settings.Credential, cancellationToken).ConfigureAwait(false);
        return new CacheIdentity(identity, _fingerprint);
    }

    private StorytellerConfigurationException UnexpectedNotModified(DocumentIdentity identity)
    {
        return new StorytellerConfigurationException(
            $"The platform answered 304 Not Modified to a read of {identity.Key} that sent no tag the provider can apply.",
            identity.Key,
            304,
            null,
            true);
    }

    // The platform answered with a 200 or a 304.
    private void MarkAnswered(EngineLifetime lifetime)
    {
        lock (_lock)
        {
            if (_lifetime == lifetime)
            {
                _lastSucceededAt = Now();
            }
        }
    }

    private void Succeed(EngineLifetime lifetime)
    {
        lock (_lock)
        {
            if (_lifetime == lifetime)
            {
                _consecutiveFailures = 0;
            }
        }
    }

    private void Fail(EngineLifetime lifetime, StorytellerConfigurationException error)
    {
        lock (_lock)
        {
            if (_lifetime == lifetime)
            {
                RecordFailure(error);
            }
        }
    }

    // Under _lock.
    private void RecordFailure(StorytellerConfigurationException error)
    {
        _lastError = error;
        _lastErrorAt = Now();
        _consecutiveFailures++;
        _totalFailures++;
    }

    private readonly record struct RunResult(RunState State, StorytellerConfigurationException? Error = null);

    // RequireCurrent: built on the snapshot the run started from, so it is swapped in only if that one is still current.
    private sealed record Draft(ConfigurationSnapshot Snapshot, bool RequireCurrent);

    // One pass of the lane: the snapshot, and the providers attached when it started.
    private sealed record Pass(EngineLifetime Lifetime, ConfigurationSnapshot Snapshot, StorytellerConfigurationProvider[] Providers);

    // A TryRefreshAsync run that has not taken the gate yet; callers until then share it.
    private sealed class QueuedRefresh
    {
        public QueuedRefresh(EngineLifetime lifetime)
        {
            Lifetime = lifetime;
        }

        public EngineLifetime Lifetime { get; }

        // Guarded by the engine's lock: a caller waits for it from inside a notification, so it must not wait for the
        // lane.
        public bool IsNested { get; set; }

        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
