using System.Collections.ObjectModel;
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
// catalog's transport, and the descendant informer: a polling source per enumeration over the same reader. The
// ancestors of a key, and of the descendants of a list, are read as who am I reads its own, through AncestorReader; the
// informer reads those of a target that requires them active in every poll.
internal sealed class StorytellerCatalog : IStorytellerCatalog
{
    // Reads at once: of one list, and of one poll.
    private const int MaxConcurrentReads = 4;

    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);

    private static readonly IReadOnlyDictionary<string, AncestorRead> NoAncestors = ReadOnlyDictionary<string, AncestorRead>.Empty;

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

    // Who am I for any key, read on every call: the key as GetAsync reads it, then its ancestors at once, as who am I
    // reads its own. ReadAt is when the read started.
    public async Task<StorytellerExecutionContext?> GetWithAncestorsAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = CatalogKeys.Parse(key, _settings.Key, nameof(key));
        var readAt = _settings.TimeProvider.GetUtcNow();

        if (await _reader.FindAsync(fullKey, cancellationToken).ConfigureAwait(false) is not { } annotation)
        {
            return null;
        }

        var reads = await AncestorReader.ReadAllAsync(
                fullKey,
                [],
                ancestor => _reader.ReadAsync(ancestor, includeDeleted: false, CancellationToken.None, cancellationToken))
            .ConfigureAwait(false);

        return AncestorReader.ContextOf(fullKey, annotation, reads, readAt);
    }

    public async Task<IReadOnlyList<TAnnotation>> ListAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation
    {
        var scope = DescendantScope.Create(target, _settings.Key);
        using var limiter = new SemaphoreSlim(MaxConcurrentReads, MaxConcurrentReads);
        var listing = await ListAsync(scope, limiter, withAncestors: false, cancellationToken).ConfigureAwait(false);

        return listing.Descendants
            .Select(descendant => descendant.Annotation)
            .Cast<TAnnotation>()
            .ToList()
            .AsReadOnly();
    }

    // The list of ListAsync, then who am I of each descendant, from one read of each distinct ancestor. ReadAt is when
    // the call started, the same in every context.
    public async Task<IReadOnlyList<StorytellerExecutionContext>> ListWithAncestorsAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation
    {
        var scope = DescendantScope.Create(target, _settings.Key);
        var readAt = _settings.TimeProvider.GetUtcNow();
        using var limiter = new SemaphoreSlim(MaxConcurrentReads, MaxConcurrentReads);
        var listing = await ListAsync(scope, limiter, withAncestors: true, cancellationToken).ConfigureAwait(false);

        return listing.Descendants
            .Select(descendant => AncestorReader.ContextOf(descendant.Key, descendant.Annotation, listing.Ancestors, readAt))
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

    // pollsOnce: the one poll has no next one to try again, so its failure is thrown rather than an empty answer. A
    // definitive failure before any poll completed (a target that does not exist, a credential or a machine that may not
    // list it, or may not read an ancestor that the target requires active) is thrown too: polling again would not fix
    // it, and a watch that never ends would read as an empty target. After a poll completed, every failure is logged and
    // polled again, as a policy may change back.
    private static async IAsyncEnumerable<StorytellerAnnotationChange<TAnnotation>> WatchAsync<TAnnotation>(DescendantPollingSource source, DescendantScope scope, bool pollsOnce, [EnumeratorCancellation] CancellationToken cancellationToken)
        where TAnnotation : StorytellerAnnotation
    {
        var hasCompleted = false;

        await foreach (var batch in source.ReadAsync(scope, cancellationToken).ConfigureAwait(false))
        {
            if (batch.Failure is { } failure)
            {
                if (pollsOnce || (!hasCompleted && !failure.IsTransient))
                {
                    ExceptionDispatchInfo.Throw(failure);
                }
            }
            else
            {
                hasCompleted = true;
            }

            foreach (var change in batch.Changes)
            {
                yield return new StorytellerAnnotationChange<TAnnotation>(change.Kind, (TAnnotation)change.Annotation, change.Removal);
            }
        }
    }

    // What both lists read: the target's ancestor, every page of its list, and each descendant that passes the filters;
    // the descendants it keeps are in the order of their keys. Then, withAncestors or for a target that requires active
    // ancestors, each distinct ancestor of those descendants. Such a target keeps the descendants whose ancestors are
    // all active, at the time that the descendants' own status is told by.
    private async Task<Listing> ListAsync(DescendantScope scope, SemaphoreSlim limiter, bool withAncestors, CancellationToken cancellationToken)
    {
        var ancestor = await ReadAncestorAsync(scope.Ancestor, cancellationToken).ConfigureAwait(false);

        // A machine restricted below the ancestor goes on: the list answers the same 403, in the words of what listing
        // needs.
        if (ancestor is { IsMissing: true, Failure: { } missing })
        {
            throw scope.DoesNotExist(missing);
        }

        var items = await ListPaging.ReadAllAsync(
                token => PollReads.LimitAsync(
                    limiter,
                    () => _reader.ReadDescendantsPageAsync(scope.Ancestor, scope.Type, scope.IncludeInactive, token, CancellationToken.None, cancellationToken),
                    cancellationToken),
                scope.Ancestor.ToString(),
                scope.ListText)
            .ConfigureAwait(false);

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        var reads = new List<Task<Listed?>>();

        foreach (var item in items.Values)
        {
            if (AnnotationKey.TryParse(item.AnnotationKey, out var key)
                && scope.IsListed(key)
                && scope.Passes(key)
                && scope.KeyOf(key) is var fullKey
                && keys.Add(fullKey.ToString()))
            {
                reads.Add(PollReads.LimitAsync(limiter, () => ReadListedAsync(fullKey, cancellationToken), cancellationToken));
            }
        }

        // Every read ends before the first failure is thrown, so none is left unobserved.
        var listed = await Task.WhenAll(reads).ConfigureAwait(false);
        var now = _settings.TimeProvider.GetUtcNow();
        var descendants = listed
            .OfType<Listed>()
            .Where(descendant => scope.Accepts(descendant.Annotation, now))
            .OrderBy(descendant => descendant.Annotation.Key, StringComparer.Ordinal)
            .ToList();

        if (!withAncestors && !scope.RequireActiveAncestors)
        {
            return new Listing(descendants, NoAncestors);
        }

        var ancestors = await ReadAncestorsAsync(descendants, ancestor, limiter, cancellationToken).ConfigureAwait(false);

        if (!scope.RequireActiveAncestors)
        {
            return new Listing(descendants, ancestors);
        }

        // The first one by key, so the same failure is thrown whatever the order the reads ended in.
        var restricted = ancestors.Values.Where(read => read.IsRestricted).MinBy(read => read.Key.ToString(), StringComparer.Ordinal);

        if (restricted is { Failure: { } failure })
        {
            throw scope.CannotReadAncestor(restricted.Key, failure);
        }

        return new Listing(descendants.Where(descendant => AncestorReader.AreActive(descendant.Key, ancestors, now)).ToList(), ancestors);
    }

    // Every distinct ancestor of the descendants, by full key, each read once, at most MaxConcurrentReads at once.
    // ancestor: the target's own, which the list read already and which is an ancestor of every descendant.
    private Task<IReadOnlyDictionary<string, AncestorRead>> ReadAncestorsAsync(IReadOnlyList<Listed> descendants, AncestorRead ancestor, SemaphoreSlim limiter, CancellationToken cancellationToken)
    {
        return AncestorReader.ReadDistinctAsync(
            descendants.Select(descendant => descendant.Key),
            ancestor,
            key => PollReads.LimitAsync(limiter, () => ReadAncestorAsync(key, cancellationToken), cancellationToken));
    }

    // An ancestor of a list, in any status but deleted, as who am I reads its own: read, restricted or missing.
    private Task<AncestorRead> ReadAncestorAsync(FullKey key, CancellationToken cancellationToken)
    {
        return AncestorReader.ReadAsync(key, ancestor => _reader.ReadAsync(ancestor, includeDeleted: false, CancellationToken.None, cancellationToken));
    }

    // A listed annotation, gone since the list (404) is left out, as a deleted one.
    private async Task<Listed?> ReadListedAsync(FullKey key, CancellationToken cancellationToken)
    {
        try
        {
            return new Listed(key, await _reader.ReadAsync(key, includeDeleted: false, CancellationToken.None, cancellationToken).ConfigureAwait(false));
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsMissing(exception))
        {
            return null;
        }
    }

    // A descendant that a list read, with its full key.
    private sealed record Listed(FullKey Key, StorytellerAnnotation Annotation);

    // What a list read. Ancestors: the reads of the descendants' ancestors, by full key; empty when the list read none.
    private sealed record Listing(IReadOnlyList<Listed> Descendants, IReadOnlyDictionary<string, AncestorRead> Ancestors);
}
