using System.Collections.Concurrent;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Configuring.Logging;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Configuring.References;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Configuring.Transport;
using _42.Platform.Storyteller.Sdk.Credentials;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.Documents;

// Documents on demand, with the transport, the reader and the apply pipeline of the provider, but without its poller and
// its disk cache. Every call revalidates with If-None-Match; a 304 returns the document from memory, with its expired
// references resolved again. Concurrent calls for one identity share one request, through in-flight tasks kept apart
// from the memory cache, so sharing works even when the cache is full.
internal sealed class StorytellerDocuments : IStorytellerDocuments, IDisposable
{
    // Entries, each of size 1.
    internal const int CacheSizeLimit = 1024;

    // The category an application filters on.
    private const string LogCategory = "_42.Platform.Storyteller.Sdk.Configuring.StorytellerDocuments";

    private readonly StorytellerSettings _settings;
    private readonly ILogger _logger;
    private readonly StorytellerTransport _transport;
    private readonly DocumentReader _reader;
    private readonly ReferenceSubstitution _resolved;
    private readonly ReferenceSubstitution _effective;
    private readonly MemoryCache _cache;
    private readonly TimeSpan? _slidingExpiration;
    private readonly ConcurrentDictionary<DocumentIdentity, Task<DocumentEntry>> _inFlight = new();
    private readonly CancellationTokenSource _disposing = new();
    private readonly CancellationToken _disposingToken;
    private int _isDisposed;

    // loggerFactory: the container's, used when the options set none.
    public StorytellerDocuments(StorytellerSettings settings, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings = settings;

        // A factory the container disposed before a late call must not fail that call.
        _logger = new SwitchableLoggerFactory(settings.LoggerFactory ?? loggerFactory).CreateLogger(LogCategory);
        _transport = TransportFactory.Create(settings);
        _reader = new DocumentReader(_transport.Client, _logger);

        // Resolved values live in the entries only: an entry that is evicted, expires or is replaced takes its secrets
        // with it, and a document reuses the fresh values of its own previous entry.
        _resolved = new ReferenceSubstitution(settings.ReferenceResolver, StorytellerReadMode.Resolved, settings.SecretRefreshInterval, settings.TimeProvider, sharesValues: false);
        _effective = new ReferenceSubstitution(null, StorytellerReadMode.Effective, settings.SecretRefreshInterval, settings.TimeProvider, sharesValues: false);
        _cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = CacheSizeLimit, Clock = new TimeProviderClock(settings.TimeProvider) });

        // Twice SecretRefreshInterval: the entries hold resolved secrets. An infinite interval, or one too long to double,
        // leaves them to the size limit.
        _slidingExpiration = settings.SecretRefreshInterval == Timeout.InfiniteTimeSpan || settings.SecretRefreshInterval > TimeSpan.MaxValue / 2
            ? null
            : settings.SecretRefreshInterval * 2;
        _disposingToken = _disposing.Token;
    }

    // The memory cache; tests fill it.
    internal MemoryCache Cache => _cache;

    private bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    public Task<StorytellerDocument> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return GetAsync(key, _settings.Mode, cancellationToken);
    }

    public Task<StorytellerDocument> GetAsync(string key, StorytellerReadMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a read mode.");
        }

        var identity = DocumentIdentity.Create(_settings.Endpoint, ParseKey(key), mode);
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        return GetSharedAsync(identity, cancellationToken);
    }

    // Cancels the requests in flight, then closes the connections and drops the documents.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        _disposing.Cancel();
        _transport.Dispose();
        _cache.Dispose();
        _disposing.Dispose();
    }

    // A task that nobody may wait for any more: a read abandoned at ReadTimeout, or a request whose callers cancelled.
    private static void Observe(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static StorytellerConfigurationException Failed(DocumentIdentity identity, string message, bool isTransient, int? statusCode = null, Exception? innerException = null)
    {
        return new StorytellerConfigurationException(message, identity.Key, statusCode, null, isTransient, innerException);
    }

    // A full key, or an annotation key in the organization, project and view of the options' Key.
    private FullKey ParseKey(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var text = key.Trim();

        if (text.StartsWith(Constants.FullKeyStartChar))
        {
            return FullKey.TryParse(text, out var fullKey)
                ? fullKey
                : throw new ArgumentException($"'{text}' is not a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}.", nameof(key));
        }

        if (!AnnotationKey.TryParse(text, out var annotationKey))
        {
            throw new ArgumentException(
                $"'{text}' is neither a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}, nor an annotation key.",
                nameof(key));
        }

        return _settings.Key is { } scope
            ? FullKey.Create(annotationKey, scope)
            : throw new ArgumentException(
                $"'{text}' is an annotation key, and there is no default scope for it: pass a full key, or set {StorytellerVariables.KEY} or {nameof(StorytellerConfigurationOptions)}.{nameof(StorytellerConfigurationOptions.Key)}.",
                nameof(key));
    }

    private async Task<StorytellerDocument> GetSharedAsync(DocumentIdentity identity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The request belongs to every caller that shares it: a caller's token ends its own wait only.
        var entry = await Share(identity).WaitAsync(cancellationToken).ConfigureAwait(false);
        return new StorytellerDocument(entry);
    }

    private Task<DocumentEntry> Share(DocumentIdentity identity)
    {
        var completion = new TaskCompletionSource<DocumentEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        var shared = _inFlight.GetOrAdd(identity, completion.Task);

        if (ReferenceEquals(shared, completion.Task))
        {
            // Every caller may have stopped waiting by the time it fails.
            Observe(completion.Task);

            // Off the caller's SynchronizationContext, and not cancelled by any one caller.
            _ = Task.Run(() => RunAsync(identity, completion), CancellationToken.None);
        }

        return shared;
    }

    private async Task RunAsync(DocumentIdentity identity, TaskCompletionSource<DocumentEntry> completion)
    {
        DocumentEntry? entry = null;
        Exception? failure = null;

        try
        {
            entry = await ReadAndApplyAsync(identity).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        // Before the callers go on: a call that starts after this one completed sends a request of its own.
        _inFlight.TryRemove(KeyValuePair.Create(identity, completion.Task));

        if (failure is null)
        {
            completion.SetResult(entry!);
        }
        else
        {
            completion.SetException(failure);
        }
    }

    private async Task<DocumentEntry> ReadAndApplyAsync(DocumentIdentity identity)
    {
        using var scope = new RunScope(_settings.ReadTimeout, _disposingToken, CancellationToken.None);

        try
        {
            // Taken once: a 304 applies this entry even if the size limit dropped it meanwhile.
            var cached = _cache.Get<DocumentEntry>(identity);

            switch (await ReadAsync(scope, identity, cached?.ETag).ConfigureAwait(false))
            {
                case ReadOutcome.Changed changed:
                {
                    var entry = await ApplyAsync(identity, changed.Document, changed.ETag, cached?.Substitution, scope).ConfigureAwait(false);
                    Store(entry);
                    _logger.LogDebug("Read version {Version} of the configuration {Key}.", changed.Document.Version, identity.Key);
                    _reader.WarnUnboundBindings(identity, changed.Document.Content as JToken);
                    Substitution(identity.Mode).WarnUnresolved(_logger, identity.Key, entry.Substitution);
                    return entry;
                }

                case ReadOutcome.NotModified notModified when cached is not null:
                {
                    var entry = await RevalidateAsync(cached, notModified, scope).ConfigureAwait(false);
                    Store(entry);
                    return entry;
                }

                case ReadOutcome.NotModified:
                    throw Failed(identity, $"The platform answered 304 Not Modified to a read of {identity.Key} that sent no tag.", true, 304);

                case ReadOutcome.Failure failure:
                    // The identity is gone or not ours to read: its entry goes, and with it the only copy of its secrets.
                    if (failure is ReadOutcome.Withheld || failure.StatusCode is 401 or 403 or 404)
                    {
                        _cache.Remove(identity);
                    }

                    throw failure.ToException(identity.Key);

                default:
                    throw new InvalidOperationException($"Unknown read outcome {identity}.");
            }
        }
        catch (OperationCanceledException exception) when (scope.IsTimedOut)
        {
            throw Failed(identity, $"Reading {identity.Key} did not finish within the ReadTimeout of {_settings.ReadTimeout}.", true, innerException: exception);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException && IsDisposed)
        {
            throw new ObjectDisposedException("The Storyteller documents were disposed.", exception);
        }
        catch (ObjectDisposedException exception)
        {
            throw Failed(identity, $"Reading {identity.Key} failed: its connection was closed.", true, innerException: exception);
        }
    }

    // A handler that ignores cancellation cannot hold the request past ReadTimeout.
    private async Task<ReadOutcome> ReadAsync(RunScope scope, DocumentIdentity identity, string? etag)
    {
        var read = _reader.ReadAsync(identity, etag, scope.Token);

        try
        {
            return await read.WaitAsync(scope.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Observe(read);
            throw;
        }
    }

    // A 304: the cached document under the answer's tag. References older than SecretRefreshInterval are resolved again;
    // fresh ones are reused.
    private async Task<DocumentEntry> RevalidateAsync(DocumentEntry cached, ReadOutcome.NotModified notModified, RunScope scope)
    {
        if (cached.Substitution is { } substitution && Substitution(cached.Identity.Mode).IsRefreshDue(substitution))
        {
            return await ApplyAsync(cached.Identity, cached.Document, notModified.ETag, substitution, scope).ConfigureAwait(false);
        }

        return string.Equals(notModified.ETag, cached.ETag, StringComparison.Ordinal)
            ? cached
            : cached.WithETag(notModified.ETag);
    }

    // reuse: the substitution of the identity's previous entry.
    private async Task<DocumentEntry> ApplyAsync(DocumentIdentity identity, Configuration document, string? etag, SubstitutionResult? reuse, RunScope scope)
    {
        var (data, substitution) = await ContentApplication.ApplyAsync(Substitution(identity.Mode), identity, document, scope, _settings.ReadTimeout, reuse).ConfigureAwait(false);
        return new DocumentEntry(identity, etag, document, substitution, data);
    }

    // Also after a 304: it adds an entry back that the size limit dropped. Over the limit, the cache drops a new entry.
    private void Store(DocumentEntry entry)
    {
        _cache.Set(entry.Identity, entry, new MemoryCacheEntryOptions { Size = 1, SlidingExpiration = _slidingExpiration });
    }

    private ReferenceSubstitution Substitution(StorytellerReadMode mode)
    {
        return mode == StorytellerReadMode.Resolved ? _resolved : _effective;
    }

    // The memory cache's expirations follow the TimeProvider of the options.
    private sealed class TimeProviderClock : ISystemClock
    {
        private readonly TimeProvider _timeProvider;

        public TimeProviderClock(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
        }

        public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();
    }
}
