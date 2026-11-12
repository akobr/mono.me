using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Annotations;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Annotating;

// The bulk create of the annotations of a view that are missing (IAnnotationBulkWriter). Every key is decided as a
// sequential import with Existing = Skip decides it (ImportAnnotationAsync, in the order of an import), and the keys to
// create are written with the items of a create (CreateCoreAsync: the same entities, stamps, audit entries and relation
// arrays), but from reads of many items and in waves of transactional batches instead of a dozen requests per key.
public partial class CosmosAnnotationService : IAnnotationBulkWriter
{
    // Runs of the whole procedure after a concurrent change or a throttle left after the retries of the client.
    private const int BulkMaxAttempts = 5;

    public async Task<AnnotationBulkResult> CreateMissingAnnotationsAsync(
        string organization,
        string project,
        string view,
        IReadOnlyList<Annotation> annotations,
        AuditActor actor,
        AnnotationBulkOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new AnnotationBulkOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxParallelBatches, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaxUnitsPerPlan, 1);

        var inputs = ReadBulkInputs(project, view, annotations);
        var results = new ViewImportResult?[inputs.Count];
        var candidates = new List<BulkInput>();
        var failed = new HashSet<string>(StringComparer.Ordinal);

        // the order of an import (depth, type, key), so ancestors come first; a duplicate stays out, as its first
        // occurrence counts
        foreach (var input in InImportOrder(inputs.Where(input => input.Failure?.ErrorCode != ErrorCodes.DuplicateFile), input => input.Key))
        {
            if (input.Failure is not null)
            {
                failed.Add(input.KeyText);
                continue;
            }

            // what hangs on an annotation that failed would create it with defaults
            var failedAncestors = input.Ancestors.Where(ancestor => failed.Contains(ancestor.ToString())).ToList();

            if (failedAncestors.Count > 0)
            {
                results[input.Index] = DependencyFailedResult(failedAncestors);
                failed.Add(input.KeyText);
                continue;
            }

            candidates.Add(input);
        }

        foreach (var input in inputs.Where(input => input.Failure is not null))
        {
            results[input.Index] = input.Failure;
        }

        var store = Store(organization);
        var commits = new BulkCommits();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var outcome = await CreateMissingCoreAsync(store, project, view, candidates, actor, options, commits, cancellationToken);

                foreach (var (index, result) in outcome.Results)
                {
                    results[index] = result;
                }

                return new AnnotationBulkResult(
                    inputs.Select(input => new AnnotationBulkItem(input.Key, results[input.Index]!)).ToList(),
                    outcome.AncestorsCreated,
                    outcome.RelationUpdates);
            }
            catch (AnnotationConcurrencyException exception)
            {
                // read and decided again: what this call committed and still reads active is still reported created
                if (attempt >= BulkMaxAttempts)
                {
                    throw new ConflictException($"The annotations changed concurrently; run the bulk create again. {exception.Message}");
                }
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.TooManyRequests && attempt < BulkMaxAttempts)
            {
                await Task.Delay(exception.RetryAfter ?? TimeSpan.FromSeconds(attempt), _timeProvider, cancellationToken);
            }
        }
    }

    // The order of the annotation pass of an import: depth in the graph, type, key.
    private static IOrderedEnumerable<T> InImportOrder<T>(IEnumerable<T> items, Func<T, AnnotationKey> getKey)
    {
        return items
            .OrderBy(item => getKey(item).GetDepth())
            .ThenBy(item => getKey(item).Type)
            .ThenBy(item => getKey(item).ToString(), StringComparer.Ordinal);
    }

    // The input in input order: its key, and its model for the write or the failure decided without a read (a key given
    // twice, an annotation that breaks the rules). The project and view of the call win.
    private static List<BulkInput> ReadBulkInputs(string project, string view, IReadOnlyList<Annotation> annotations)
    {
        var inputs = new List<BulkInput>(annotations.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < annotations.Count; index++)
        {
            var annotation = annotations[index];

            if (!AnnotationKey.TryParse(annotation.AnnotationKey, out var key))
            {
                throw new InvalidInputException($"'{annotation.AnnotationKey}' is not an annotation key.", ErrorCodes.InvalidKey);
            }

            var keyText = key.ToString();
            var ancestors = key.GetAllAncestorKeys();

            if (!seen.Add(keyText))
            {
                inputs.Add(new BulkInput(index, key, keyText, ancestors, null, new ViewImportResult(
                    ViewImportOutcome.Failed,
                    ErrorCode: ErrorCodes.DuplicateFile,
                    Message: $"The input holds the annotation {keyText} more than once; only the first counts.")));
                continue;
            }

            var problems = AnnotationRules.GetProblems(annotation);

            if (problems.Count > 0)
            {
                inputs.Add(new BulkInput(index, key, keyText, ancestors, null, new ViewImportResult(
                    ViewImportOutcome.Failed,
                    ErrorCode: ErrorCodes.InvalidAnnotation,
                    Message: $"The annotation '{keyText}' is not valid: {string.Join(" ", problems)}")));
                continue;
            }

            var model = AnnotationFactory.Normalize(annotation with { ProjectName = project, ViewName = view }, key);
            inputs.Add(new BulkInput(index, key, keyText, ancestors, model, null));
        }

        return inputs;
    }

    // One run of the procedure: read the closure of the candidates, decide them in order, compute the relation arrays
    // set-wise, and (unless a dry run) write the creates in waves by depth and the arrays of active parents last.
    private async Task<BulkOutcome> CreateMissingCoreAsync(
        AnnotationItemStore store,
        string project,
        string view,
        IReadOnlyList<BulkInput> candidates,
        AuditActor actor,
        AnnotationBulkOptions options,
        BulkCommits commits,
        CancellationToken cancellationToken)
    {
        var organization = store.Repository.Organization;
        var closure = new Dictionary<string, AnnotationKey>(StringComparer.Ordinal);

        foreach (var input in candidates)
        {
            foreach (var key in input.Ancestors)
            {
                closure.TryAdd(key.ToString(), key);
            }

            closure.TryAdd(input.KeyText, input.Key);
        }

        var nodes = await store.ReadNodesAsync(
            closure.Values.Select(key => FullKey.Create(key, organization, project, view)),
            withAudit: false,
            cancellationToken);

        // a key an earlier attempt committed that still reads active counts as missing, its state before this call, so
        // it is decided (and reported) as it was then, and not written again; one deleted or purged since counts as it
        // reads now, so nothing is created under a deleted ancestor and a purged one is written again
        bool IsKept(string keyText) => commits.HasKey(keyText) && nodes[keyText].State == AnnotationItemState.Active;
        AnnotationItemState StateOf(string keyText) => IsKept(keyText) ? AnnotationItemState.Missing : nodes[keyText].State;

        // in a dry run, a key an earlier step of it made active is not stored, but counts as an active ancestor, as the
        // overlay of an import does
        bool IsAssumedActive(string keyText) => options.DryRun
                                                && options.DryRunActiveKeys?.Contains(keyText) == true
                                                && nodes[keyText].State != AnnotationItemState.Active;

        var results = new Dictionary<int, ViewImportResult>();
        var creates = new List<BulkCreate>();
        var marked = new HashSet<string>(StringComparer.Ordinal);

        foreach (var input in candidates)
        {
            var node = nodes[input.KeyText];

            if (StateOf(input.KeyText) == AnnotationItemState.Active)
            {
                var changes = AnnotationAuditDiff.Compute(node.Entity!.ToModel(), input.Model!, _audit.MaxValueLength);
                results[input.Index] = changes.IsEmpty ? new ViewImportResult(ViewImportOutcome.Unchanged) : DocumentExistsResult(input.Key);
                continue;
            }

            // the keys the create writes, ancestors first; an ancestor an earlier key of the input creates is not among them
            var written = input.Ancestors
                .Where(ancestor => StateOf(ancestor.ToString()) != AnnotationItemState.Active
                                   && !marked.Contains(ancestor.ToString())
                                   && !IsAssumedActive(ancestor.ToString()))
                .Append(input.Key)
                .ToList();
            var deleted = written.Where(key => StateOf(key.ToString()) == AnnotationItemState.Deleted).ToList();

            if (deleted.Count > 0)
            {
                results[input.Index] = DeletedSkippedResult(deleted);
                continue;
            }

            // a missing ancestor gets the defaults of the first key that needs it, as in a create of that key
            var ancestors = written.Take(written.Count - 1).ToList();

            foreach (var ancestor in ancestors)
            {
                var ancestorText = ancestor.ToString();
                creates.Add(new BulkCreate(nodes[ancestorText], AnnotationFactory.CreateDefault(ancestor, project, view, input.Model), input.Key));
                marked.Add(ancestorText);
            }

            creates.Add(new BulkCreate(node, input.Model!, null));
            marked.Add(input.KeyText);
            results[input.Index] = new ViewImportResult(ViewImportOutcome.Created, Warnings: ancestors.Count > 0 ? [AncestorCreatedWarning(ancestors)] : null);
        }

        var now = _timeProvider.GetUtcNow();
        var stamp = Stamp(actor, now);
        var toWrite = creates.Where(create => !IsKept(create.Node.KeyText)).ToList();
        var entities = toWrite.ToDictionary(
            create => create.Node.KeyText,
            create => ToStoredEntity(create.Model, organization) with { Created = stamp, Modified = stamp },
            StringComparer.Ordinal);
        var updates = AddBulkRelations(closure.Values, nodes, toWrite, entities);

        var ancestorsCreated = creates.Where(create => create.Cause is not null).Select(create => create.Node.KeyText).ToList();

        // an ancestor that an earlier attempt committed for a key that a concurrent change decided otherwise since
        var leftOver = commits.Keys.Where(key => !marked.Contains(key)).ToList();

        if (leftOver.Count > 0)
        {
            var inputKeys = candidates.Select(input => input.KeyText).ToHashSet(StringComparer.Ordinal);
            ancestorsCreated.AddRange(leftOver.Where(key => !inputKeys.Contains(key)).Order(StringComparer.Ordinal));
        }

        if (options.DryRun)
        {
            // an assumed parent has nothing stored to replace, but the real run replaces its arrays once
            var assumedParents = toWrite
                .SelectMany(create => AnnotationRelations.GetParentRelations(create.Node.Key.Annotation))
                .Select(relation => relation.Parent.ToString())
                .Where(parentText => !entities.ContainsKey(parentText) && IsAssumedActive(parentText))
                .Distinct(StringComparer.Ordinal)
                .Count();

            return new BulkOutcome(results, ancestorsCreated, updates.Count + assumedParents);
        }

        await store.LoadAuditsAsync(toWrite.Select(create => create.Node), cancellationToken);

        var writes = toWrite
            .Select(create => new PendingWrite(
                create.Node,
                entities[create.Node.KeyText],
                AnnotationAuditStore.NewEntry(AnnotationAuditAction.Created, actor, now) with
                {
                    Cause = create.Cause is null ? null : new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Ancestor, Key = create.Cause },
                },
                PendingKind.Create))
            .ToList();

        await WriteBulkCreatesAsync(store, writes, options, commits, cancellationToken);
        await WriteBulkRelationsAsync(store, updates, options, commits, cancellationToken);

        return new BulkOutcome(results, ancestorsCreated, commits.ParentCount);
    }

    // The names the parents gain, set-wise: a created parent lists its created children; an active parent gets the
    // created children it lacks, after the active children of the closure it does not list (what a failed relations wave
    // of an earlier run left out). Fills the created entities, and returns the replaced active ones.
    private static List<(AnnotationNode Node, AnnotationEntity Entity)> AddBulkRelations(
        IEnumerable<AnnotationKey> closure,
        IReadOnlyDictionary<string, AnnotationNode> nodes,
        IReadOnlyList<BulkCreate> toWrite,
        Dictionary<string, AnnotationEntity> entities)
    {
        var gained = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.Ordinal);

        void Gain(AnnotationKey child, Func<string, bool> accepts)
        {
            foreach (var (parent, array, value) in AnnotationRelations.GetParentRelations(child))
            {
                var parentText = parent.ToString();

                if (!accepts(parentText))
                {
                    continue;
                }

                if (!gained.TryGetValue(parentText, out var arrays))
                {
                    arrays = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                    gained[parentText] = arrays;
                }

                if (!arrays.TryGetValue(array, out var names))
                {
                    names = [];
                    arrays[array] = names;
                }

                names.Add(value);
            }
        }

        foreach (var key in InImportOrder(closure, key => key))
        {
            if (nodes[key.ToString()].State == AnnotationItemState.Active)
            {
                Gain(key, parentText => nodes[parentText].State == AnnotationItemState.Active);
            }
        }

        foreach (var create in toWrite)
        {
            Gain(create.Node.Key.Annotation, parentText => entities.ContainsKey(parentText) || nodes[parentText].State == AnnotationItemState.Active);
        }

        var updates = new List<(AnnotationNode Node, AnnotationEntity Entity)>();

        foreach (var (parentText, arrays) in gained)
        {
            var isCreated = entities.TryGetValue(parentText, out var entity);
            var current = isCreated ? entity! : nodes[parentText].Entity!;
            var updated = arrays.Aggregate(current, (result, array) => AnnotationRelations.AddRange(result, array.Key, array.Value));

            if (isCreated)
            {
                entities[parentText] = updated;
            }
            else if (!ReferenceEquals(updated, current))
            {
                updates.Add((nodes[parentText], updated));
            }
        }

        return updates;
    }

    // The creates in waves by depth (roots first), each wave in plans of at most MaxUnitsPerPlan annotations, so only
    // one plan's items are serialized at a time. A unit is the annotation and its audit, in one partition.
    private async Task WriteBulkCreatesAsync(
        AnnotationItemStore store,
        IReadOnlyList<PendingWrite> writes,
        AnnotationBulkOptions options,
        BulkCommits commits,
        CancellationToken cancellationToken)
    {
        var waves = writes
            .Select((write, order) => (Write: write, Order: order))
            .GroupBy(unit => unit.Write.Node.Key.Annotation.GetDepth())
            .OrderBy(wave => wave.Key);

        foreach (var wave in waves)
        {
            // a partition's units together, so they share as few batches as possible
            var units = wave
                .OrderBy(unit => unit.Write.Node.PartitionKey, StringComparer.Ordinal)
                .ThenBy(unit => unit.Order)
                .Select(unit => unit.Write);

            foreach (var chunk in units.Chunk(options.MaxUnitsPerPlan))
            {
                using var plan = new BatchPlan(store.Repository, options.MaxParallelBatches);

                foreach (var write in chunk)
                {
                    var node = write.Node;
                    var keyText = node.KeyText;
                    var stream = plan.Keep(_serializer.WriteAnnotation(write.Entity));
                    var audit = plan.Keep(_audit.Prepare(node, [write.Entry], timeToLiveSeconds: null, out _));
                    plan.Add(
                        wave.Key,
                        node.PartitionKey,
                        stream.Length + audit.Length,
                        new BatchOperation(batch => batch.CreateItemStream(stream), _ => commits.AddKey(keyText)),
                        new BatchOperation(AnnotationAuditStore.Write(node, audit)));
                }

                await plan.ExecuteAsync(cancellationToken);
                options.Progress?.Report(commits.KeyCount);
            }
        }
    }

    // The arrays of active parents, each replaced under its ETag (no audit entry, Modified kept), as AddRelationUpdates
    // writes them.
    private async Task WriteBulkRelationsAsync(
        AnnotationItemStore store,
        IReadOnlyList<(AnnotationNode Node, AnnotationEntity Entity)> updates,
        AnnotationBulkOptions options,
        BulkCommits commits,
        CancellationToken cancellationToken)
    {
        var ordered = updates.OrderBy(update => update.Node.PartitionKey, StringComparer.Ordinal);

        foreach (var chunk in ordered.Chunk(options.MaxUnitsPerPlan))
        {
            using var plan = new BatchPlan(store.Repository, options.MaxParallelBatches);

            foreach (var (node, entity) in chunk)
            {
                var keyText = node.KeyText;
                var etag = node.ETag;
                var stream = plan.Keep(_serializer.WriteAnnotation(entity));
                plan.Add(
                    RelationsWave,
                    node.PartitionKey,
                    stream.Length,
                    new BatchOperation(
                        batch => batch.ReplaceItemStream(node.AnnotationId, stream, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag }),
                        _ => commits.AddParent(keyText)));
            }

            await plan.ExecuteAsync(cancellationToken);
        }
    }

    // An annotation of the input. Failure: decided without a read; otherwise Model is the normalized annotation.
    private sealed record class BulkInput(
        int Index,
        AnnotationKey Key,
        string KeyText,
        IReadOnlyList<AnnotationKey> Ancestors,
        Annotation? Model,
        ViewImportResult? Failure);

    // A key the call creates, with its model; Cause: the input key whose missing ancestor it is.
    private sealed record class BulkCreate(AnnotationNode Node, Annotation Model, AnnotationKey? Cause);

    private sealed record class BulkOutcome(
        IReadOnlyDictionary<int, ViewImportResult> Results,
        IReadOnlyList<string> AncestorsCreated,
        int RelationUpdates);

    // What the attempts of one call committed, from the callbacks of batches that run in parallel: the created keys,
    // and the parents whose arrays were replaced.
    private sealed class BulkCommits
    {
        private readonly ConcurrentDictionary<string, byte> _keys = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, byte> _parents = new(StringComparer.Ordinal);

        public int KeyCount => _keys.Count;

        public int ParentCount => _parents.Count;

        public IEnumerable<string> Keys => _keys.Keys;

        public bool HasKey(string keyText) => _keys.ContainsKey(keyText);

        public void AddKey(string keyText) => _keys.TryAdd(keyText, 0);

        public void AddParent(string keyText) => _parents.TryAdd(keyText, 0);
    }
}
