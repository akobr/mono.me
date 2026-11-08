using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Logging;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Hosting.Informer;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Hosting.Catalog;

// The annotations of any type and the descendants of any annotation, through point reads and list pages of the
// catalog's transport, and the descendant informer: a polling source per enumeration over the same reader.
internal sealed class StorytellerCatalog : IStorytellerCatalog
{
    // Reads at once: of one list, and of one poll.
    private const int MaxConcurrentReads = 4;

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);

    private readonly StorytellerSettings _settings;
    private readonly CatalogReader _reader;
    private readonly ILogger _informerLogger;

    // loggerFactory: the container's, used when the options set none.
    public StorytellerCatalog(StorytellerSettings settings, CatalogReader reader, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(reader);

        _settings = settings;
        _reader = reader;

        // A factory the container disposed before a late poll must not fail that poll.
        _informerLogger = new SwitchableLoggerFactory(settings.LoggerFactory ?? loggerFactory).CreateLogger(SubscriptionCatalog.InformerLogCategory);
    }

    public Task<StorytellerAnnotation?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = CatalogKeys.Parse(key, _settings.Key, nameof(key));
        return _reader.FindAsync(fullKey, cancellationToken);
    }

    // The mapper builds the snapshot of the key's type, so a key of TAnnotation's type reads as a TAnnotation.
    // StorytellerAnnotation takes any key; a type of the application that derives from it names no annotation type,
    // and throws as Find<T>() of who am I does.
    public async Task<TAnnotation?> GetAsync<TAnnotation>(string key, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation
    {
        AnnotationType? type = typeof(TAnnotation) == typeof(StorytellerAnnotation)
            ? null
            : SnapshotTypes.Require<TAnnotation>(nameof(TAnnotation));
        var fullKey = CatalogKeys.Parse(key, _settings.Key, nameof(key));

        if (type is { } expected && fullKey.Annotation.Type != expected)
        {
            throw new ArgumentException(
                $"'{fullKey}' does not name {AnnotationTypeFacts.Singular(expected)}, {AnnotationTypeFacts.Pattern(expected)}.",
                nameof(key));
        }

        return (TAnnotation?)await _reader.FindAsync(fullKey, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TAnnotation>> ListAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation
    {
        var scope = DescendantScope.Create(target, _settings.Key);
        using var limiter = new SemaphoreSlim(MaxConcurrentReads, MaxConcurrentReads);

        try
        {
            await _reader.ReadAsync(scope.Ancestor, includeDeleted: false, CancellationToken.None, cancellationToken).ConfigureAwait(false);
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsMissing(exception))
        {
            throw scope.DoesNotExist(exception);
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsRestricted(exception))
        {
            // A machine restricted below the ancestor: the list answers the same 403, in the words of what listing
            // needs.
        }

        var items = await ListPaging.ReadAllAsync(
                token => LimitAsync(
                    limiter,
                    () => _reader.ReadDescendantsPageAsync(scope.Ancestor, scope.Type, scope.IncludeInactive, token, CancellationToken.None, cancellationToken),
                    cancellationToken),
                scope.Ancestor.ToString(),
                scope.ListText)
            .ConfigureAwait(false);

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        var reads = new List<Task<StorytellerAnnotation?>>();

        foreach (var item in items.Values)
        {
            if (AnnotationKey.TryParse(item.AnnotationKey, out var key)
                && scope.IsListed(key)
                && scope.Passes(key)
                && keys.Add(scope.KeyOf(key).ToString()))
            {
                var fullKey = scope.KeyOf(key);
                reads.Add(LimitAsync(limiter, () => ReadListedAsync(fullKey, cancellationToken), cancellationToken));
            }
        }

        // Every read ends before the first failure is thrown, so none is left unobserved.
        var annotations = await Task.WhenAll(reads).ConfigureAwait(false);
        var now = _settings.TimeProvider.GetUtcNow();

        return annotations
            .OfType<StorytellerAnnotation>()
            .Where(annotation => scope.Accepts(annotation, now))
            .OrderBy(annotation => annotation.Key, StringComparer.Ordinal)
            .Cast<TAnnotation>()
            .ToList()
            .AsReadOnly();
    }

    // The arguments are checked at the call, not at the first MoveNextAsync. The interval follows the rule of the
    // subscriptions' PollInterval, as WatchExecutionsAsync's does.
    public IAsyncEnumerable<StorytellerAnnotationChange<TAnnotation>> WatchAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation
    {
        ArgumentNullException.ThrowIfNull(target);

        var interval = pollInterval ?? DefaultPollInterval;

        if (!PollingSubscriptionSource.IsValidPollInterval(interval))
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), interval, $"The poll interval must be {PollingSubscriptionSource.PollIntervalRule}.");
        }

        var scope = DescendantScope.Create(target, _settings.Key);
        var source = new DescendantPollingSource(_reader, interval, MaxConcurrentReads, _settings.TimeProvider, _settings.NextRandom, _informerLogger);
        return WatchAsync<TAnnotation>(source, scope, pollsOnce: interval == TimeSpan.Zero, cancellationToken);
    }

    // pollsOnce: the one poll has no next one to try again, so its failure is thrown rather than an empty answer.
    private static async IAsyncEnumerable<StorytellerAnnotationChange<TAnnotation>> WatchAsync<TAnnotation>(DescendantPollingSource source, DescendantScope scope, bool pollsOnce, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TAnnotation : StorytellerAnnotation
    {
        await foreach (var batch in source.ReadAsync(scope, cancellationToken).ConfigureAwait(false))
        {
            if (batch.Failure is { } failure && pollsOnce)
            {
                ExceptionDispatchInfo.Throw(failure);
            }

            foreach (var change in batch.Changes)
            {
                yield return new StorytellerAnnotationChange<TAnnotation>(change.Kind, (TAnnotation)change.Annotation, change.Removal);
            }
        }
    }

    private static async Task<T> LimitAsync<T>(SemaphoreSlim limiter, Func<Task<T>> read, CancellationToken cancellationToken)
    {
        await limiter.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return await read().ConfigureAwait(false);
        }
        finally
        {
            limiter.Release();
        }
    }

    // A listed annotation, gone since the list (404) is left out, as a deleted one.
    private async Task<StorytellerAnnotation?> ReadListedAsync(FullKey key, CancellationToken cancellationToken)
    {
        try
        {
            return await _reader.ReadAsync(key, includeDeleted: false, CancellationToken.None, cancellationToken).ConfigureAwait(false);
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsMissing(exception))
        {
            return null;
        }
    }
}
