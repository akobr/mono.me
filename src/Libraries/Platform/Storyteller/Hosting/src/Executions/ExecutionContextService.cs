using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Logging;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Hosting.Executions;

// Who am I, stale while revalidate: the first GetAsync reads and its callers share that read; later calls return the
// snapshot at once, and the first call after RefreshInterval starts one background read, so a refresh never blocks a
// caller. Reads are numbered: a read that ends after a newer one does not replace the newer snapshot.
internal sealed class ExecutionContextService : IExecutionContext, IDisposable
{
    // The category an application filters on.
    private const string LogCategory = "_42.Platform.Storyteller.Sdk.Hosting.ExecutionContext";

    private readonly FullKey? _key;
    private readonly CatalogReader _reader;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan? _refreshInterval;
    private readonly ILogger _logger;
    private readonly Lock _lock = new();
    private readonly CancellationTokenSource _disposing = new();
    private readonly CancellationToken _disposingToken;
    private StorytellerExecutionContext? _snapshot;
    private long _snapshotRead;
    private long _reads;
    private Task<StorytellerExecutionContext>? _firstRead;
    private Task _backgroundRead = Task.CompletedTask;
    private DateTimeOffset _backgroundStartedAt = DateTimeOffset.MinValue;
    private int _isDisposed;

    // loggerFactory: the container's, used when the options set none.
    public ExecutionContextService(StorytellerSettings settings, CatalogReader reader, StorytellerExecutionContextOptions options, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(options);

        _key = settings.Key is { } key ? CatalogKeys.Normalize(key) : null;
        _reader = reader;
        _timeProvider = settings.TimeProvider;

        // Zero and infinite: only RefreshAsync reads again.
        _refreshInterval = options.RefreshInterval > TimeSpan.Zero ? options.RefreshInterval : null;

        // A factory the container disposed before a late background read must not fail that read.
        _logger = new SwitchableLoggerFactory(settings.LoggerFactory ?? loggerFactory).CreateLogger(LogCategory);
        _disposingToken = _disposing.Token;
    }

    // The background read started last; tests wait for it.
    internal Task BackgroundRead
    {
        get
        {
            lock (_lock)
            {
                return _backgroundRead;
            }
        }
    }

    private bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    public Task<StorytellerExecutionContext> GetAsync(CancellationToken cancellationToken = default)
    {
        var key = RequireKey();
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        Task<StorytellerExecutionContext> firstRead;

        lock (_lock)
        {
            if (_snapshot is { } snapshot)
            {
                StartBackgroundReadWhenDue(key, snapshot);
                return Task.FromResult(snapshot);
            }

            // Task.Run never runs inline, and the read clears _firstRead under this lock, so it is set first.
            firstRead = _firstRead ??= Observe(Task.Run(() => ReadFirstAsync(key), CancellationToken.None));
        }

        // The read belongs to every caller that waits for it: a caller's token ends its own wait only.
        return firstRead.WaitAsync(cancellationToken);
    }

    public Task<StorytellerExecutionContext> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var key = RequireKey();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return ReadAndPublishAsync(key, rereadUnreadable: true, isBackground: false, cancellationToken);
    }

    // Cancels the reads in flight.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _disposing.Cancel();
        _disposing.Dispose();
    }

    private static T Observe<T>(T task)
        where T : Task
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    private FullKey RequireKey()
    {
        return _key ?? throw new StorytellerConfigurationException(
            $"Who am I needs the key of the process: set {StorytellerVariables.KEY} or {nameof(StorytellerConfigurationOptions)}.{nameof(StorytellerConfigurationOptions.Key)} to the full key of its annotation, @{{organization}}.{{project}}.{{view}}.{{annotation key}}.");
    }

    // A failed first read is not kept: the next call reads again.
    private async Task<StorytellerExecutionContext> ReadFirstAsync(FullKey key)
    {
        try
        {
            return await ReadAndPublishAsync(key, rereadUnreadable: true, isBackground: false, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            lock (_lock)
            {
                _firstRead = null;
            }
        }
    }

    // Under the lock. One background read at a time, and one per interval, so a failing platform is not asked on every
    // call.
    private void StartBackgroundReadWhenDue(FullKey key, StorytellerExecutionContext snapshot)
    {
        if (_refreshInterval is not { } interval || !_backgroundRead.IsCompleted)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var last = snapshot.ReadAt > _backgroundStartedAt ? snapshot.ReadAt : _backgroundStartedAt;

        if (now - last < interval)
        {
            return;
        }

        _backgroundStartedAt = now;
        _backgroundRead = Observe(Task.Run(() => ReadInBackgroundAsync(key), CancellationToken.None));
    }

    // A failure keeps the snapshot; only a key that is gone replaces it, as deleted.
    private async Task ReadInBackgroundAsync(FullKey key)
    {
        try
        {
            await ReadAndPublishAsync(key, rereadUnreadable: false, isBackground: true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            // Stopped with the container.
        }
        catch (StorytellerConfigurationException exception)
        {
            _logger.LogWarning(
                "Refreshing who am I ({Key}) failed ({Class}); the snapshot read at {ReadAt} stays. {Error}",
                key.ToString(),
                exception.IsTransient ? "transient" : "definitive",
                Volatile.Read(ref _snapshot)?.ReadAt,
                exception.Message);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Refreshing who am I ({Key}) failed unexpectedly; the snapshot stays.", key.ToString());
        }
    }

    // rereadUnreadable: also the ancestors the previous snapshot could not read. isBackground: a key that is gone (404)
    // turns the previous snapshot deleted instead of failing.
    private async Task<StorytellerExecutionContext> ReadAndPublishAsync(FullKey key, bool rereadUnreadable, bool isBackground, CancellationToken cancellationToken)
    {
        long number;
        StorytellerExecutionContext? previous;

        lock (_lock)
        {
            number = ++_reads;
            previous = _snapshot;
        }

        var readAt = _timeProvider.GetUtcNow();
        StorytellerExecutionContext snapshot;

        try
        {
            IReadOnlyList<string> skipped = rereadUnreadable || previous is null ? [] : previous.Unreadable;
            snapshot = await ReadAsync(key, skipped, readAt, cancellationToken).ConfigureAwait(false);
        }
        catch (StorytellerConfigurationException exception) when (isBackground && previous is not null && CatalogReader.IsMissing(exception))
        {
            _logger.LogWarning("The annotation {Key} of who am I is gone; it is reported as deleted.", key.ToString());
            snapshot = previous.WithKeyGoneAt(readAt);
        }

        lock (_lock)
        {
            if (number > _snapshotRead)
            {
                _snapshot = snapshot;
                _snapshotRead = number;
            }

            return _snapshot!;
        }
    }

    // The key with deleted and inactive annotations included, then its ancestors at once. skipped: ancestors the
    // credential may not read, kept unread.
    private async Task<StorytellerExecutionContext> ReadAsync(FullKey key, IReadOnlyList<string> skipped, DateTimeOffset readAt, CancellationToken cancellationToken)
    {
        var annotation = await _reader.ReadAsync(key, includeDeleted: true, _disposingToken, cancellationToken).ConfigureAwait(false);
        var ancestorKeys = key.Annotation.GetAllAncestorKeys().Select(ancestor => FullKey.Create(ancestor, key)).ToList();
        var reads = ancestorKeys
            .Where(ancestor => !skipped.Contains(ancestor.ToString(), StringComparer.Ordinal))
            .Select(ancestor => ReadAncestorAsync(ancestor, cancellationToken))
            .ToList();

        // Every read ends before the first failure is thrown, so none is left unobserved.
        var results = (await Task.WhenAll(reads).ConfigureAwait(false)).ToDictionary(result => result.Key, StringComparer.Ordinal);
        var ancestors = new Dictionary<AnnotationType, StorytellerAnnotation>();
        var unreadable = new List<string>();
        var restricted = new List<string>();

        // In the order of the ancestors, roots first.
        foreach (var ancestor in ancestorKeys.Select(ancestor => ancestor.ToString()))
        {
            if (!results.TryGetValue(ancestor, out var result))
            {
                unreadable.Add(ancestor);
            }
            else if (result.Annotation is { } read)
            {
                ancestors[read.Type] = read;
            }
            else if (result.IsRestricted)
            {
                unreadable.Add(ancestor);
                restricted.Add(ancestor);
            }
        }

        if (restricted.Count > 0)
        {
            _logger.LogInformation(
                "Who am I ({Key}) goes on without {Count} ancestors the machine may not read, {Ancestors}: a machine restricted to an annotation reads that annotation and its descendants only. They are not read again until RefreshAsync.",
                key.ToString(),
                restricted.Count,
                string.Join(", ", restricted));
        }

        return new StorytellerExecutionContext
        {
            Key = key.ToString(),
            Annotation = annotation,
            Ancestors = ancestors,
            Unreadable = unreadable,
            ReadAt = readAt,
        };
    }

    // A restricted ancestor is unreadable, a missing one (deleted after the key was read) is left out; any other failure
    // fails the read as a whole.
    private async Task<AncestorRead> ReadAncestorAsync(FullKey key, CancellationToken cancellationToken)
    {
        var text = key.ToString();

        try
        {
            return new AncestorRead(text, await _reader.ReadAsync(key, includeDeleted: false, _disposingToken, cancellationToken).ConfigureAwait(false), false);
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsRestricted(exception))
        {
            return new AncestorRead(text, null, true);
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsMissing(exception))
        {
            return new AncestorRead(text, null, false);
        }
    }

    private sealed record AncestorRead(string Key, StorytellerAnnotation? Annotation, bool IsRestricted);
}
