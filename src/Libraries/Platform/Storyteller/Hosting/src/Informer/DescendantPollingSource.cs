using System.Runtime.CompilerServices;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// The informer of a catalog's target, by polling. A poll lists the descendants of the target's type under its ancestor,
// keeps those that pass its filters, and diffs them against the held ones by the fingerprints of their list items.
// Snapshots and removal reasons come from point reads, as list values are lossy. Reads run at most maxConcurrentReads
// at once. Each complete poll yields one batch; a failed poll yields its failure, logs a warning and keeps what is held.
// The next poll starts the interval, with the provider's jitter, after the previous one completed; a zero interval
// polls once. The join of executions with their usages is PollingSubscriptionSource's; the two share ListPaging.
internal sealed class DescendantPollingSource
{
    private readonly IAnnotationPages _pages;
    private readonly TimeSpan _pollInterval;
    private readonly int _maxConcurrentReads;
    private readonly TimeProvider _timeProvider;
    private readonly Func<double> _nextRandom;
    private readonly ILogger _logger;

    // pollInterval: as PollingSubscriptionSource's. nextRandom: uniform in [0, 1), for the jitter.
    public DescendantPollingSource(IAnnotationPages pages, TimeSpan pollInterval, int maxConcurrentReads, TimeProvider timeProvider, Func<double> nextRandom, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(pages);

        if (!PollingSubscriptionSource.IsValidPollInterval(pollInterval))
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), pollInterval, $"The poll interval must be {PollingSubscriptionSource.PollIntervalRule}.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentReads, 1);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(nextRandom);
        ArgumentNullException.ThrowIfNull(logger);

        _pages = pages;
        _pollInterval = pollInterval;
        _maxConcurrentReads = maxConcurrentReads;
        _timeProvider = timeProvider;
        _nextRandom = nextRandom;
        _logger = logger;
    }

    // Each enumeration starts from nothing held. Ends with OperationCanceledException when the token is cancelled.
    public async IAsyncEnumerable<DescendantBatch> ReadAsync(DescendantScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        using var limiter = new SemaphoreSlim(_maxConcurrentReads, _maxConcurrentReads);
        var poll = new PollState(scope, limiter);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DescendantBatch batch;

            try
            {
                batch = await PollAsync(poll, cancellationToken).ConfigureAwait(false);
            }
            catch (StorytellerConfigurationException exception) when (cancellationToken.IsCancellationRequested)
            {
                // A read may fail while another one of the poll still runs, and the poll throws only after all of them
                // ended. A stop in that time ends the enumeration as a stop, not with the failure.
                throw new OperationCanceledException($"The poll of {scope} was cancelled.", exception, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();

            yield return batch;

            if (_pollInterval == TimeSpan.Zero)
            {
                yield break;
            }

            var delay = StorytellerConfigurationEngine.ComputeDelay(_pollInterval, _nextRandom());
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    // Why a confirmation read ends a held annotation; null while it is still a descendant of the target. A failure other
    // than a 404 has no answer and is handled before.
    private static StorytellerRemovalReason? ReasonOf(PointRead read, bool includeInactive, DateTimeOffset now)
    {
        if (read.Annotation is not { } annotation)
        {
            return StorytellerRemovalReason.Missing;
        }

        if (annotation.DeletedAt is not null)
        {
            return StorytellerRemovalReason.Deleted;
        }

        if (includeInactive)
        {
            return null;
        }

        return annotation.GetStatus(now) switch
        {
            AnnotationStatus.Disabled => StorytellerRemovalReason.Disabled,
            AnnotationStatus.Scheduled => StorytellerRemovalReason.Scheduled,
            AnnotationStatus.Expired => StorytellerRemovalReason.Expired,
            _ => null,
        };
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

    private async Task<DescendantBatch> PollAsync(PollState poll, CancellationToken cancellationToken)
    {
        var scope = poll.Scope;
        IReadOnlyDictionary<string, Annotation> items;

        try
        {
            if (!poll.HasCompleted)
            {
                await CheckAncestorAsync(poll, cancellationToken).ConfigureAwait(false);
            }

            items = await ListPaging.ReadAllAsync(
                    token => LimitAsync(
                        poll.Limiter,
                        () => _pages.ReadDescendantsPageAsync(scope.Ancestor, scope.Type, scope.IncludeInactive, token, cancellationToken),
                        cancellationToken),
                    scope.Ancestor.ToString(),
                    scope.ListText)
                .ConfigureAwait(false);
        }
        catch (StorytellerConfigurationException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Polling {Target} failed ({Class}); the {Count} annotations held stay as they are. {Error}",
                scope.ToString(),
                exception.IsTransient ? "transient" : "definitive",
                poll.Held.Count,
                exception.Message);
            return DescendantBatch.Failed(exception);
        }

        var candidates = Candidates(poll, items);
        var changes = await DiffAsync(poll, candidates, cancellationToken).ConfigureAwait(false);
        poll.HasCompleted = true;

        return new DescendantBatch(changes);
    }

    // The list of an ancestor that does not exist is empty, which would read as no descendants for ever. A machine
    // restricted below the ancestor may not read it: the list answers the same 403, in the words of what listing needs.
    private async Task CheckAncestorAsync(PollState poll, CancellationToken cancellationToken)
    {
        try
        {
            await LimitAsync(poll.Limiter, () => _pages.ReadAnnotationAsync(poll.Scope.Ancestor, includeDeleted: false, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsMissing(exception))
        {
            throw poll.Scope.DoesNotExist(exception);
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsRestricted(exception))
        {
            _logger.LogDebug("The machine may not read {Key}; the list tells what it needs.", poll.Scope.Ancestor.ToString());
        }
    }

    // The listed descendants that pass the filters, by full key.
    private Dictionary<string, Candidate> Candidates(PollState poll, IReadOnlyDictionary<string, Annotation> items)
    {
        var scope = poll.Scope;
        var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);

        foreach (var item in items.Values)
        {
            if (!AnnotationKey.TryParse(item.AnnotationKey, out var key) || !scope.IsListed(key))
            {
                _logger.LogDebug("The list of {Target} holds {Key}, which is none of them; it is skipped.", scope.ToString(), item.AnnotationKey);
                continue;
            }

            if (scope.Passes(key))
            {
                var fullKey = scope.KeyOf(key);
                candidates[fullKey.ToString()] = new Candidate(fullKey, ListFingerprint.Of(item));
            }
        }

        return candidates;
    }

    // The reads of a poll run at once, up to the limit; the changes are applied after all of them ended, Removed first,
    // then Changed, then Added, each by key.
    private async Task<IReadOnlyList<StorytellerAnnotationChange<StorytellerAnnotation>>> DiffAsync(PollState poll, Dictionary<string, Candidate> candidates, CancellationToken cancellationToken)
    {
        var additions = new List<(string Key, Task<Held?> Read)>();
        var updates = new List<(string Key, Task<Held?> Read)>();
        var removals = new List<(string Key, Task<StorytellerRemoval?> Read)>();

        foreach (var (key, candidate) in candidates)
        {
            if (!poll.Held.TryGetValue(key, out var held))
            {
                additions.Add((key, ReadCandidateAsync(poll, candidate, cancellationToken)));
            }
            else if (!string.Equals(held.Fingerprint, candidate.Fingerprint, StringComparison.Ordinal))
            {
                updates.Add((key, ReadCandidateAsync(poll, candidate, cancellationToken)));
            }
        }

        foreach (var (key, held) in poll.Held)
        {
            if (!candidates.ContainsKey(key))
            {
                removals.Add((key, ConfirmAsync(poll, held, cancellationToken)));
            }
        }

        // Every read ends before a cancellation is thrown.
        await Task.WhenAll(additions.Select(item => (Task)item.Read)
                .Concat(updates.Select(item => item.Read))
                .Concat(removals.Select(item => item.Read)))
            .ConfigureAwait(false);

        var changes = new List<StorytellerAnnotationChange<StorytellerAnnotation>>();

        foreach (var (key, read) in removals.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (await read.ConfigureAwait(false) is { } removal && poll.Held.Remove(key, out var held))
            {
                changes.Add(new StorytellerAnnotationChange<StorytellerAnnotation>(StorytellerChangeKind.Removed, held.Annotation, removal));
            }
        }

        foreach (var (key, read) in updates.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (await read.ConfigureAwait(false) is { } next)
            {
                poll.Held[key] = next;
                changes.Add(new StorytellerAnnotationChange<StorytellerAnnotation>(StorytellerChangeKind.Changed, next.Annotation));
            }
        }

        foreach (var (key, read) in additions.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (await read.ConfigureAwait(false) is { } next)
            {
                poll.Held[key] = next;
                changes.Add(new StorytellerAnnotationChange<StorytellerAnnotation>(StorytellerChangeKind.Added, next.Annotation));
            }
        }

        return changes.AsReadOnly();
    }

    // A candidate not held, or held with another fingerprint: held from now when its read is accepted. Otherwise what is
    // held stays as it was, fingerprint included, so the next poll reads it again while it is listed.
    private async Task<Held?> ReadCandidateAsync(PollState poll, Candidate candidate, CancellationToken cancellationToken)
    {
        var read = await ReadAsync(poll, candidate.Key, includeDeleted: false, cancellationToken).ConfigureAwait(false);

        if (read.Failure is { } failure)
        {
            LogFailedRead(read.Key, failure);
            return null;
        }

        var annotation = read.Annotation!;
        var now = _timeProvider.GetUtcNow();

        if (poll.Scope.Accepts(annotation, now))
        {
            return new Held(candidate.Key, annotation, candidate.Fingerprint);
        }

        _logger.LogDebug(
            "{Key} was listed as active, but reads as {Status}; the next poll diffs it again.",
            candidate.Key.ToString(),
            annotation.GetStatus(now));
        return null;
    }

    // A held annotation that is not listed any more: removed when it is gone, deleted, or inactive while inactive ones are
    // not included. Otherwise the list missed it, and it stays.
    private async Task<StorytellerRemoval?> ConfirmAsync(PollState poll, Held held, CancellationToken cancellationToken)
    {
        var read = await ReadAsync(poll, held.Key, includeDeleted: true, cancellationToken).ConfigureAwait(false);

        if (read.Failure is { } failure && !CatalogReader.IsMissing(failure))
        {
            LogFailedRead(read.Key, failure);
            return null;
        }

        if (ReasonOf(read, poll.Scope.IncludeInactive, _timeProvider.GetUtcNow()) is { } reason)
        {
            return new StorytellerRemoval(held.Key.Annotation.ToString(), reason);
        }

        _logger.LogDebug("{Key} was not listed, but reads as listed; it stays.", held.Key.ToString());
        return null;
    }

    // Reads that fail are answers too, so one failure leaves the other reads of the poll alone.
    private async Task<PointRead> ReadAsync(PollState poll, FullKey key, bool includeDeleted, CancellationToken cancellationToken)
    {
        try
        {
            var annotation = await LimitAsync(poll.Limiter, () => _pages.ReadAnnotationAsync(key, includeDeleted, cancellationToken), cancellationToken).ConfigureAwait(false);
            return new PointRead(key, annotation, null);
        }
        catch (StorytellerConfigurationException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new PointRead(key, null, exception);
        }
    }

    // An annotation gone since the list is no surprise: the next poll's list tells.
    private void LogFailedRead(FullKey key, StorytellerConfigurationException failure)
    {
        if (CatalogReader.IsMissing(failure))
        {
            _logger.LogDebug("{Key} is gone since it was listed; the next poll diffs it again.", key.ToString());
            return;
        }

        _logger.LogWarning(
            "Reading {Key} failed ({Class}); it stays as it was, and the next poll reads it again. {Error}",
            key.ToString(),
            failure.IsTransient ? "transient" : "definitive",
            failure.Message);
    }

    // What one enumeration holds: the annotations it reported, by full key, and whether a poll completed.
    private sealed class PollState
    {
        public PollState(DescendantScope scope, SemaphoreSlim limiter)
        {
            Scope = scope;
            Limiter = limiter;
        }

        public DescendantScope Scope { get; }

        public SemaphoreSlim Limiter { get; }

        public Dictionary<string, Held> Held { get; } = new(StringComparer.Ordinal);

        public bool HasCompleted { get; set; }
    }

    // A listed descendant that passes the filters, with the fingerprint of its list item.
    private sealed record Candidate(FullKey Key, string Fingerprint);

    // An annotation the source reported, with the fingerprint of the list item it was read for.
    private sealed record Held(FullKey Key, StorytellerAnnotation Annotation, string Fingerprint);

    // A point read: the annotation, or the failure.
    private sealed record PointRead(FullKey Key, StorytellerAnnotation? Annotation, StorytellerConfigurationException? Failure);
}
