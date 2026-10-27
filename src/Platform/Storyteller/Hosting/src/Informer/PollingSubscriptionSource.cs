using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// The informer by polling. A poll lists the active executions and usages of the responsibility, joins them into the
// candidates of the context (an execution in it whose usage is listed), and diffs those against the held subscriptions
// by the fingerprints of their list items. Snapshots and removal reasons come from point reads, as list values are
// lossy. Reads run at most maxConcurrentReads at once. Each complete poll yields one batch with every held key dirty; a
// failed poll yields its failure, logs a warning and keeps what is held. The next poll starts the interval, with the
// provider's jitter, after the previous one completed; a zero interval polls once.
internal sealed class PollingSubscriptionSource : ISubscriptionChangeSource
{
    // A list that pages on past this is a platform that answers its first page again.
    internal const int MaxPages = 1000;

    // The contexts a warning names at most.
    private const int MaxNamedContexts = 10;

    private readonly IAnnotationPages _pages;
    private readonly TimeSpan _pollInterval;
    private readonly int _maxConcurrentReads;
    private readonly TimeProvider _timeProvider;
    private readonly Func<double> _nextRandom;
    private readonly ILogger _logger;

    // nextRandom: uniform in [0, 1), for the jitter.
    public PollingSubscriptionSource(IAnnotationPages pages, TimeSpan pollInterval, int maxConcurrentReads, TimeProvider timeProvider, Func<double> nextRandom, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentOutOfRangeException.ThrowIfLessThan(pollInterval, TimeSpan.Zero);
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

    public async IAsyncEnumerable<SubscriptionBatch> ReadAsync(SubscriptionScope scope, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        using var limiter = new SemaphoreSlim(_maxConcurrentReads, _maxConcurrentReads);
        var poll = new PollState(scope, limiter);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubscriptionBatch batch;

            try
            {
                batch = await PollAsync(poll, cancellationToken).ConfigureAwait(false);
            }
            catch (StorytellerConfigurationException exception) when (cancellationToken.IsCancellationRequested)
            {
                // A read may fail while another one of the poll still runs, and the poll throws only after all of them
                // ended. A stop in that time ends the enumeration as a stop, not with the failure.
                throw new OperationCanceledException("The poll of the subscriptions was cancelled.", exception, cancellationToken);
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

    private static bool IsActive(StorytellerAnnotation annotation, DateTimeOffset now)
    {
        return annotation.DeletedAt is null && annotation.GetStatus(now) == AnnotationStatus.Active;
    }

    // Why a confirmation read ends a subscription; null while the annotation is active. A failure other than a 404 has
    // no answer and is handled before.
    private static StorytellerRemovalReason? ReasonOf(PointRead read, DateTimeOffset now)
    {
        if (read.Annotation is not { } annotation)
        {
            return StorytellerRemovalReason.Missing;
        }

        if (annotation.DeletedAt is not null)
        {
            return StorytellerRemovalReason.Deleted;
        }

        return annotation.GetStatus(now) switch
        {
            AnnotationStatus.Disabled => StorytellerRemovalReason.Disabled,
            AnnotationStatus.Scheduled => StorytellerRemovalReason.Scheduled,
            AnnotationStatus.Expired => StorytellerRemovalReason.Expired,
            _ => null,
        };
    }

    private static StorytellerConfigurationException Unending(SubscriptionScope scope, AnnotationList list, string reason)
    {
        var key = scope.ResponsibilityKey.ToString();
        return new StorytellerConfigurationException(
            $"Listing the {list.ToString().ToLowerInvariant()} of {key} did not end: {reason}. A Storyteller API without the fix that pages annotation lists through their continuation tokens answers the first page again; update the API.",
            key,
            null,
            null,
            false);
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

    private async Task<SubscriptionBatch> PollAsync(PollState poll, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, Annotation> executions;
        IReadOnlyDictionary<string, Annotation> usages;

        try
        {
            if (!poll.HasCompleted)
            {
                await CheckResponsibilityAsync(poll, cancellationToken).ConfigureAwait(false);
            }

            var executionList = ReadListAsync(poll, AnnotationList.Executions, cancellationToken);
            var usageList = ReadListAsync(poll, AnnotationList.Usages, cancellationToken);

            // Both end before a failure is thrown, so none is left unobserved. A failure seen after a stop escapes the
            // filter below, and ReadAsync turns it into the stop.
            await Task.WhenAll(executionList, usageList).ConfigureAwait(false);
            executions = await executionList.ConfigureAwait(false);
            usages = await usageList.ConfigureAwait(false);
        }
        catch (StorytellerConfigurationException exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Polling the subscriptions of {Scope} failed ({Class}); the {Count} subscriptions held stay as they are. {Error}",
                poll.Scope.ToString(),
                exception.IsTransient ? "transient" : "definitive",
                poll.Held.Count,
                exception.Message);
            return SubscriptionBatch.Failed(exception);
        }

        var candidates = Join(poll, executions, usages);
        var changes = await DiffAsync(poll, candidates, cancellationToken).ConfigureAwait(false);
        poll.HasCompleted = true;

        return new SubscriptionBatch(changes, poll.Held.Keys.Order(StringComparer.Ordinal).ToList().AsReadOnly());
    }

    // The lists of a responsibility that does not exist are empty, which would read as no subscriptions for ever.
    private async Task CheckResponsibilityAsync(PollState poll, CancellationToken cancellationToken)
    {
        var key = poll.Scope.ResponsibilityKey;

        try
        {
            await LimitAsync(poll.Limiter, () => _pages.ReadAnnotationAsync(key, includeDeleted: false, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsMissing(exception))
        {
            throw new StorytellerConfigurationException(
                $"The responsibility {poll.Scope.Responsibility} does not exist: {key} was not found, and the lists of a responsibility that does not exist are empty. Create it, or watch a responsibility that exists.",
                key.ToString(),
                404,
                null,
                false,
                exception);
        }
    }

    // Every page, by annotation key; pages have no order, so a key listed twice keeps the item listed last.
    private async Task<IReadOnlyDictionary<string, Annotation>> ReadListAsync(PollState poll, AnnotationList list, CancellationToken cancellationToken)
    {
        var items = new Dictionary<string, Annotation>(StringComparer.Ordinal);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;

        for (var pages = 1; ; pages++)
        {
            var from = token;
            var page = await LimitAsync(
                    poll.Limiter,
                    () => _pages.ReadPageAsync(poll.Scope.ResponsibilityKey, list, from, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in page.Items)
            {
                if (!string.IsNullOrEmpty(item.AnnotationKey))
                {
                    items[item.AnnotationKey] = item;
                }
            }

            // An empty page with a token is legal: Cosmos DB may cut a page short.
            if (page.ContinuationToken is not { } next)
            {
                return items;
            }

            if (!tokens.Add(next))
            {
                throw Unending(poll.Scope, list, "the platform answered a continuation token it had answered before");
            }

            if (pages >= MaxPages)
            {
                throw Unending(poll.Scope, list, $"it had more than {MaxPages} pages");
            }

            token = next;
        }
    }

    // The executions of the responsibility in the context whose usage is listed. The platform ignores a context filter
    // of the list, so the context is filtered here.
    private Dictionary<string, Candidate> Join(PollState poll, IReadOnlyDictionary<string, Annotation> executions, IReadOnlyDictionary<string, Annotation> usages)
    {
        var scope = poll.Scope;
        var usageItems = new Dictionary<string, Annotation>(StringComparer.Ordinal);

        foreach (var item in usages.Values)
        {
            if (TryParse(item, AnnotationType.Usage, scope, out var key))
            {
                usageItems[key.SubjectName] = item;
            }
        }

        var candidates = new Dictionary<string, Candidate>(StringComparer.Ordinal);
        var contexts = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var item in executions.Values)
        {
            if (!TryParse(item, AnnotationType.Execution, scope, out var key))
            {
                continue;
            }

            contexts.Add(key.ContextName);

            if (!string.Equals(key.ContextName, scope.Context, StringComparison.Ordinal)
                || !usageItems.TryGetValue(key.SubjectName, out var usage))
            {
                continue;
            }

            var executionKey = scope.ExecutionKey(key.SubjectName);
            candidates[executionKey.ToString()] = new Candidate(
                executionKey,
                scope.UsageKey(key.SubjectName),
                ListFingerprint.Of(item),
                ListFingerprint.Of(usage));
        }

        if (!poll.HasCompleted && contexts.Count > 0 && !contexts.Contains(scope.Context))
        {
            _logger.LogWarning(
                "The first poll of {Responsibility} found executions in the contexts {Contexts}, but none in the context {Context}; check the name of the context.",
                scope.ResponsibilityKey.ToString(),
                string.Join(", ", contexts.Take(MaxNamedContexts)) + (contexts.Count > MaxNamedContexts ? $" and {contexts.Count - MaxNamedContexts} more" : string.Empty),
                scope.Context);
        }

        return candidates;
    }

    // An item of the list of the scope's responsibility, of the type of the list.
    private bool TryParse(Annotation item, AnnotationType type, SubscriptionScope scope, [NotNullWhen(true)] out AnnotationKey? key)
    {
        if (AnnotationKey.TryParse(item.AnnotationKey, out key)
            && key.Type == type
            && string.Equals(key.ResponsibilityName, scope.Responsibility, StringComparison.Ordinal))
        {
            return true;
        }

        _logger.LogDebug("The list of {Responsibility} holds {Key}, which is not one of its {Type} annotations; it is skipped.", scope.ResponsibilityKey.ToString(), item.AnnotationKey, type);
        key = null;
        return false;
    }

    // The reads of a poll run at once, up to the limit; the changes are applied after all of them ended, Removed first,
    // then Changed, then Added, each by key.
    private async Task<IReadOnlyList<StorytellerExecutionChange>> DiffAsync(PollState poll, Dictionary<string, Candidate> candidates, CancellationToken cancellationToken)
    {
        var additions = new List<(string Key, Task<Held?> Read)>();
        var updates = new List<(string Key, Task<Held?> Read)>();
        var removals = new List<(string Key, Task<StorytellerRemoval?> Read)>();

        foreach (var (key, candidate) in candidates)
        {
            if (!poll.Held.TryGetValue(key, out var held))
            {
                additions.Add((key, AddAsync(poll, candidate, cancellationToken)));
            }
            else if (!string.Equals(held.ExecutionFingerprint, candidate.ExecutionFingerprint, StringComparison.Ordinal)
                || !string.Equals(held.UsageFingerprint, candidate.UsageFingerprint, StringComparison.Ordinal))
            {
                updates.Add((key, ChangeAsync(poll, held, candidate, cancellationToken)));
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

        var changes = new List<StorytellerExecutionChange>();

        foreach (var (key, read) in removals.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (await read.ConfigureAwait(false) is { } removal && poll.Held.Remove(key, out var held))
            {
                changes.Add(new StorytellerExecutionChange(StorytellerChangeKind.Removed, held.Execution, held.Usage, removal));
            }
        }

        foreach (var (key, read) in updates.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (await read.ConfigureAwait(false) is { } next)
            {
                poll.Held[key] = next;
                changes.Add(new StorytellerExecutionChange(StorytellerChangeKind.Changed, next.Execution, next.Usage));
            }
        }

        foreach (var (key, read) in additions.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (await read.ConfigureAwait(false) is { } next)
            {
                poll.Held[key] = next;
                changes.Add(new StorytellerExecutionChange(StorytellerChangeKind.Added, next.Execution, next.Usage));
            }
        }

        return changes.AsReadOnly();
    }

    // A candidate not held: added when both its annotations read as active. Otherwise it is not held, and the next poll
    // tries again while it is listed.
    private async Task<Held?> AddAsync(PollState poll, Candidate candidate, CancellationToken cancellationToken)
    {
        var executionRead = ReadAsync(poll, candidate.ExecutionKey, includeDeleted: false, cancellationToken);
        var usageRead = ReadAsync(poll, candidate.UsageKey, includeDeleted: false, cancellationToken);
        await Task.WhenAll(executionRead, usageRead).ConfigureAwait(false);
        var execution = await executionRead.ConfigureAwait(false);
        var usage = await usageRead.ConfigureAwait(false);

        if (!IsRead(execution, candidate.ExecutionKey) | !IsRead(usage, candidate.ExecutionKey))
        {
            return null;
        }

        return IsStillActive(candidate.ExecutionKey, execution.Annotation!, usage.Annotation!)
            ? new Held(candidate, (StorytellerExecution)execution.Annotation!, usage.Annotation!)
            : null;
    }

    // A held candidate whose list items changed: reads what changed. Changed while both are active; otherwise it stays
    // as it was, fingerprints included, so the next poll diffs it again.
    private async Task<Held?> ChangeAsync(PollState poll, Held held, Candidate candidate, CancellationToken cancellationToken)
    {
        var executionRead = string.Equals(held.ExecutionFingerprint, candidate.ExecutionFingerprint, StringComparison.Ordinal)
            ? null
            : ReadAsync(poll, candidate.ExecutionKey, includeDeleted: false, cancellationToken);
        var usageRead = string.Equals(held.UsageFingerprint, candidate.UsageFingerprint, StringComparison.Ordinal)
            ? null
            : ReadAsync(poll, candidate.UsageKey, includeDeleted: false, cancellationToken);
        await Task.WhenAll(new[] { executionRead, usageRead }.OfType<Task>()).ConfigureAwait(false);
        var execution = executionRead is null ? null : await executionRead.ConfigureAwait(false);
        var usage = usageRead is null ? null : await usageRead.ConfigureAwait(false);

        if ((execution is not null && !IsRead(execution, candidate.ExecutionKey)) | (usage is not null && !IsRead(usage, candidate.ExecutionKey)))
        {
            return null;
        }

        var nextExecution = (StorytellerExecution?)execution?.Annotation ?? held.Execution;
        var nextUsage = usage?.Annotation ?? held.Usage;

        return IsStillActive(candidate.ExecutionKey, nextExecution, nextUsage)
            ? new Held(candidate, nextExecution, nextUsage)
            : null;
    }

    // A held subscription that is not a candidate any more: the first of its execution and its usage that is deleted,
    // inactive or gone ends it. Both active means the list missed it, and it stays.
    private async Task<StorytellerRemoval?> ConfirmAsync(PollState poll, Held held, CancellationToken cancellationToken)
    {
        var executionRead = ReadAsync(poll, held.ExecutionKey, includeDeleted: true, cancellationToken);
        var usageRead = ReadAsync(poll, held.UsageKey, includeDeleted: true, cancellationToken);
        await Task.WhenAll(executionRead, usageRead).ConfigureAwait(false);
        PointRead[] reads = [await executionRead.ConfigureAwait(false), await usageRead.ConfigureAwait(false)];
        var now = _timeProvider.GetUtcNow();

        foreach (var read in reads)
        {
            if (read.Failure is { } failure && !CatalogReader.IsMissing(failure))
            {
                LogFailedRead(read, held.ExecutionKey);
                return null;
            }

            if (ReasonOf(read, now) is { } reason)
            {
                return new StorytellerRemoval(read.Key.Annotation.ToString(), reason);
            }
        }

        _logger.LogDebug(
            "The subscription {Key} was not listed, but its execution and usage read as active; it stays.",
            held.ExecutionKey.ToString());
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

    private bool IsRead(PointRead read, FullKey subscription)
    {
        if (read.Failure is null)
        {
            return true;
        }

        LogFailedRead(read, subscription);
        return false;
    }

    // An annotation gone since the list is no surprise: the next poll's list tells.
    private void LogFailedRead(PointRead read, FullKey subscription)
    {
        var failure = read.Failure!;

        if (CatalogReader.IsMissing(failure))
        {
            _logger.LogDebug(
                "{Key} of the subscription {Subscription} is gone since it was listed; the next poll diffs it again.",
                read.Key.ToString(),
                subscription.ToString());
            return;
        }

        _logger.LogWarning(
            "Reading {Key} for the subscription {Subscription} failed ({Class}); the subscription stays as it was, and the next poll reads it again. {Error}",
            read.Key.ToString(),
            subscription.ToString(),
            failure.IsTransient ? "transient" : "definitive",
            failure.Message);
    }

    // The list said active by the platform's clock; the reads are checked by this process's.
    private bool IsStillActive(FullKey subscription, StorytellerAnnotation execution, StorytellerAnnotation usage)
    {
        var now = _timeProvider.GetUtcNow();

        if (IsActive(execution, now) && IsActive(usage, now))
        {
            return true;
        }

        _logger.LogDebug(
            "The subscription {Key} was listed as active, but its execution is {ExecutionStatus} and its usage {UsageStatus}; the next poll diffs it again.",
            subscription.ToString(),
            execution.GetStatus(now),
            usage.GetStatus(now));
        return false;
    }

    // What one enumeration holds: the subscriptions it reported, by the full key of the execution, and whether a poll
    // completed.
    private sealed class PollState
    {
        public PollState(SubscriptionScope scope, SemaphoreSlim limiter)
        {
            Scope = scope;
            Limiter = limiter;
        }

        public SubscriptionScope Scope { get; }

        public SemaphoreSlim Limiter { get; }

        public Dictionary<string, Held> Held { get; } = new(StringComparer.Ordinal);

        public bool HasCompleted { get; set; }
    }

    // An execution in the context whose usage is listed, with the fingerprints of both list items.
    private sealed record Candidate(FullKey ExecutionKey, FullKey UsageKey, string ExecutionFingerprint, string UsageFingerprint);

    // A subscription the source reported, with the fingerprints of the list items it was read for.
    private sealed record Held(FullKey ExecutionKey, FullKey UsageKey, StorytellerExecution Execution, StorytellerAnnotation Usage, string ExecutionFingerprint, string UsageFingerprint)
    {
        public Held(Candidate candidate, StorytellerExecution execution, StorytellerAnnotation usage)
            : this(candidate.ExecutionKey, candidate.UsageKey, execution, usage, candidate.ExecutionFingerprint, candidate.UsageFingerprint)
        {
        }
    }

    // A point read: the annotation, or the failure.
    private sealed record PointRead(FullKey Key, StorytellerAnnotation? Annotation, StorytellerConfigurationException? Failure);
}
