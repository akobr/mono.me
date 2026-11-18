using System.Runtime.CompilerServices;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// The informer of a catalog's target, by polling. A poll lists the descendants of the target's type under its ancestor,
// keeps those that pass its filters, and diffs them against the held ones by the fingerprints of their list items.
// Snapshots and removal reasons come from point reads, as list values are lossy. For a target that requires active
// ancestors, a poll also reads each distinct ancestor of the held and of the listed descendants once, after the list
// and before the diff, as an ancestor's change moves no fingerprint of its descendants: a descendant is held only
// while all its ancestors are active, and one that leaves for an ancestor names that ancestor. Reads run at most
// maxConcurrentReads at once. Each complete poll yields one batch; a failed poll yields its failure, logs a warning and
// keeps what is held. The next poll starts the interval, with the provider's jitter, after the previous one completed;
// a zero interval polls once. The join of executions with their usages is PollingSubscriptionSource's; the two share
// ListPaging.
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

    private async Task<DescendantBatch> PollAsync(PollState poll, CancellationToken cancellationToken)
    {
        var scope = poll.Scope;
        Dictionary<string, Candidate> candidates;
        PollAncestors? ancestors = null;

        try
        {
            var own = poll.HasCompleted ? null : await CheckAncestorAsync(poll, cancellationToken).ConfigureAwait(false);

            var items = await ListPaging.ReadAllAsync(
                    token => PollReads.LimitAsync(
                        poll.Limiter,
                        () => _pages.ReadDescendantsPageAsync(scope.Ancestor, scope.Type, scope.IncludeInactive, token, cancellationToken),
                        cancellationToken),
                    scope.Ancestor.ToString(),
                    scope.ListText)
                .ConfigureAwait(false);
            candidates = Candidates(poll, items);

            if (scope.RequireActiveAncestors)
            {
                ancestors = await ReadAncestorsAsync(poll, candidates, own, cancellationToken).ConfigureAwait(false);
            }
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

        var changes = await DiffAsync(poll, candidates, ancestors, cancellationToken).ConfigureAwait(false);
        poll.HasCompleted = true;

        return new DescendantBatch(changes);
    }

    // The list of an ancestor that does not exist is empty, which would read as no descendants for ever. A machine
    // restricted below the ancestor may not read it: the list answers the same 403, in the words of what listing needs.
    // Returns the read, which serves a target that requires active ancestors: the ancestor is one of every descendant.
    private async Task<AncestorRead> CheckAncestorAsync(PollState poll, CancellationToken cancellationToken)
    {
        var ancestor = await AncestorReader.ReadAsync(
                poll.Scope.Ancestor,
                key => PollReads.LimitAsync(poll.Limiter, () => _pages.ReadAnnotationAsync(key, includeDeleted: false, cancellationToken), cancellationToken))
            .ConfigureAwait(false);

        if (ancestor is { IsMissing: true, Failure: { } missing })
        {
            throw poll.Scope.DoesNotExist(missing);
        }

        if (ancestor.IsRestricted)
        {
            _logger.LogDebug("The machine may not read {Key}; the list tells what it needs.", poll.Scope.Ancestor.ToString());
        }

        return ancestor;
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

    // Each distinct ancestor of the held and of the listed descendants, read once, in any status but deleted, as who am
    // I reads its own; and the time that tells their status, one for all of them. A read that fails is an answer: what
    // is under that ancestor stays as it was. own: the target's ancestor as the existence check of this poll read it,
    // null once a poll has completed. Until then a definitive failure, a machine that may not read an ancestor
    // included, fails the poll: polling again would not fix it. The one poll of a zero interval fails with a transient
    // one too: no next poll decides again, and an answer without those descendants would read as a whole one.
    private async Task<PollAncestors> ReadAncestorsAsync(PollState poll, Dictionary<string, Candidate> candidates, AncestorRead? own, CancellationToken cancellationToken)
    {
        var descendants = candidates.Values.Select(candidate => candidate.Key)
            .Concat(poll.Held.Where(held => !candidates.ContainsKey(held.Key)).Select(held => held.Value.Key));

        // Until a poll has completed, the target's own ancestors too: they are ancestors of every descendant, so a
        // machine that may not read them ends the watch while the target has no descendants too.
        if (!poll.HasCompleted)
        {
            descendants = descendants.Append(poll.Scope.Ancestor);
        }

        // Every read ends before a cancellation is thrown.
        var reads = await AncestorReader.ReadDistinctAsync(descendants, own, key => ReadAncestorAsync(poll, key, cancellationToken)).ConfigureAwait(false);
        var failed = reads.Values.Where(read => read.IsUndecided).OrderBy(read => read.Key.ToString(), StringComparer.Ordinal).ToList();

        // The first one by key, so the same failure ends the watch whatever the order the reads ended in.
        var pollsOnce = _pollInterval == TimeSpan.Zero;

        if (!poll.HasCompleted && failed.FirstOrDefault(read => pollsOnce || read.Failure is { IsTransient: false }) is { Failure: { } failure } ending)
        {
            throw ending.IsRestricted ? poll.Scope.CannotReadAncestor(ending.Key, failure) : failure;
        }

        foreach (var read in failed)
        {
            LogFailedAncestor(poll.Scope, read);
        }

        return new PollAncestors(reads, _timeProvider.GetUtcNow());
    }

    // The reads of a poll run at once, up to the limit; the changes are applied after all of them ended, Removed first,
    // then Changed, then Added, each by key. ancestors: of a target that requires them active, null otherwise. Under
    // them a listed descendant that is not held is read only when all its ancestors are active, and a held one that an
    // ancestor ends leaves without a read, so it is not Changed too.
    private async Task<IReadOnlyList<StorytellerAnnotationChange<StorytellerAnnotation>>> DiffAsync(PollState poll, Dictionary<string, Candidate> candidates, PollAncestors? ancestors, CancellationToken cancellationToken)
    {
        var additions = new List<(string Key, Task<Held?> Read)>();
        var updates = new List<(string Key, Task<Held?> Read)>();
        var removals = new List<(string Key, Task<StorytellerRemoval?> Read)>();

        foreach (var (key, candidate) in candidates)
        {
            var ancestor = ancestors?.FindInactive(candidate.Key);

            if (!poll.Held.TryGetValue(key, out var held))
            {
                if (ancestor is null)
                {
                    additions.Add((key, ReadCandidateAsync(poll, candidate, cancellationToken)));
                }
                else
                {
                    _logger.LogDebug(
                        "{Key} is listed, but its ancestor {Ancestor} is {Status}; it is not added, and the next poll decides again.",
                        candidate.Key.ToString(),
                        ancestor.Key.ToString(),
                        ancestors?.RemovalBy(ancestor)?.Reason.ToString() ?? "not read");
                }
            }
            else if (ancestors?.RemovalBy(ancestor) is { } removal)
            {
                // It is listed, so the descendant itself stays, and the ancestor ends it.
                removals.Add((key, Task.FromResult<StorytellerRemoval?>(removal)));
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
                removals.Add((key, ConfirmAsync(poll, held, ancestors, cancellationToken)));
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
    // not included. Otherwise the list missed it, and it stays, unless an ancestor ends it: its own end goes first, as
    // the execution goes before the usage in a subscription. ancestors: as for DiffAsync.
    private async Task<StorytellerRemoval?> ConfirmAsync(PollState poll, Held held, PollAncestors? ancestors, CancellationToken cancellationToken)
    {
        var read = await ReadAsync(poll, held.Key, includeDeleted: true, cancellationToken).ConfigureAwait(false);

        if (read.Failure is { } failure && !CatalogReader.IsMissing(failure))
        {
            LogFailedRead(read.Key, failure);
            return null;
        }

        if (PollReads.ReasonOf(read.Annotation, poll.Scope.IncludeInactive, _timeProvider.GetUtcNow()) is { } reason)
        {
            return new StorytellerRemoval(held.Key.Annotation.ToString(), reason);
        }

        if (ancestors?.RemovalBy(ancestors.FindInactive(held.Key)) is { } removal)
        {
            return removal;
        }

        _logger.LogDebug("{Key} was not listed, but reads as listed; it stays.", held.Key.ToString());
        return null;
    }

    // Reads that fail are answers too, so one failure leaves the other reads of the poll alone.
    private async Task<PointRead> ReadAsync(PollState poll, FullKey key, bool includeDeleted, CancellationToken cancellationToken)
    {
        try
        {
            var annotation = await PollReads.LimitAsync(poll.Limiter, () => _pages.ReadAnnotationAsync(key, includeDeleted, cancellationToken), cancellationToken).ConfigureAwait(false);
            return new PointRead(key, annotation, null);
        }
        catch (StorytellerConfigurationException exception) when (!cancellationToken.IsCancellationRequested)
        {
            return new PointRead(key, null, exception);
        }
    }

    // An ancestor of the descendants, in any status but deleted. Its failure is an answer too.
    private async Task<AncestorRead> ReadAncestorAsync(PollState poll, FullKey key, CancellationToken cancellationToken)
    {
        var read = await ReadAsync(poll, key, includeDeleted: false, cancellationToken).ConfigureAwait(false);
        return new AncestorRead(key, read.Annotation, read.Failure);
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

    // An ancestor whose read failed with anything but a 404. A machine that may not read it is told what requiring
    // active ancestors needs.
    private void LogFailedAncestor(DescendantScope scope, AncestorRead ancestor)
    {
        if (ancestor.Failure is not { } failure)
        {
            return;
        }

        _logger.LogWarning(
            "Reading {Key} failed ({Class}); the descendants under it stay as they were, and the next poll reads it again. {Error}",
            ancestor.Key.ToString(),
            failure.IsTransient ? "transient" : "definitive",
            ancestor.IsRestricted ? scope.CannotReadAncestor(ancestor.Key, failure).Message : failure.Message);
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

    // The ancestors of the descendants of one poll as it read them, by full key, and the time that tells their status.
    private sealed record PollAncestors(IReadOnlyDictionary<string, AncestorRead> Reads, DateTimeOffset Now)
    {
        // The ancestor that keeps a descendant from being under active ancestors; null when all of its are active.
        public AncestorRead? FindInactive(FullKey descendant)
        {
            return AncestorReader.FindInactive(descendant, Reads, Now);
        }

        // Why an ancestor ends a descendant, as a confirmation read would tell it for the ancestor; null when none
        // does: all are active, or the read of one failed, which is no answer.
        public StorytellerRemoval? RemovalBy(AncestorRead? ancestor)
        {
            return ancestor is { IsUndecided: false } && PollReads.ReasonOf(ancestor.Annotation, includeInactive: false, Now) is { } reason
                ? new StorytellerRemoval(ancestor.Key.Annotation.ToString(), reason)
                : null;
        }
    }
}
