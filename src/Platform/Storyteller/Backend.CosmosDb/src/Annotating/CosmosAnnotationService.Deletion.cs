using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Annotations;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Annotating;

// Soft delete, restore, permanent delete, and the reads of deleted annotations and of the audit.
public partial class CosmosAnnotationService
{
    // waves of a delete or a purge: the deepest descendants first, the root last
    private const int RootWave = 4;

    public async Task<Annotation?> GetDeletedAnnotationAsync(FullKey fullKey)
    {
        var node = await Store(fullKey.OrganizationName).LoadAsync(fullKey, withConfiguration: false, withAudit: false);
        return node.State == AnnotationItemState.Deleted ? node.Entity!.ToModel() : null;
    }

    public async Task<IReadOnlyList<Annotation>> FindDeletedAnnotationsAsync(
        string organization,
        string project,
        string view,
        IReadOnlyCollection<AnnotationKey> keys)
    {
        var nodes = await Store(organization).ReadManyAsync(keys.Select(key => FullKey.Create(key, organization, project, view)));

        return nodes
            .Where(node => node.State == AnnotationItemState.Deleted)
            .Select(node => node.Entity!.ToModel())
            .ToList();
    }

    public async Task<AnnotationsResponse> GetDeletedAnnotationsAsync(
        string organization,
        string project,
        string view,
        bool rootsOnly = true,
        string? deletionId = null,
        string? continuationToken = null)
    {
        // every partition of the project; only annotation items carry both AnnotationType and Deletion
        var text = "SELECT * FROM c WHERE STARTSWITH(c.PartitionKey, @projectPrefix) AND c.ProjectName = @project "
                   + "AND c.ViewName = @view AND IS_DEFINED(c.AnnotationType) AND IS_DEFINED(c.Deletion)";

        if (rootsOnly)
        {
            text += " AND c.Deletion.IsRoot = true";
        }

        if (!string.IsNullOrEmpty(deletionId))
        {
            text += " AND c.Deletion.DeletionId = @deletionId";
        }

        var query = new QueryDefinition(text + " ORDER BY c.Deletion.DeletedAt DESC")
            .WithParameter("@projectPrefix", $"{project}.")
            .WithParameter("@project", project)
            .WithParameter("@view", view)
            .WithParameter("@deletionId", deletionId);

        var (entities, nextToken) = await Store(organization).QueryPageAsync(query, continuationToken);
        var annotations = entities.Select(entity => entity.ToModel()).ToList();

        return new AnnotationsResponse
        {
            Annotations = annotations,
            ContinuationToken = nextToken,
            Count = annotations.Count,
        };
    }

    public Task<AnnotationDeletionResult> DeleteAnnotationAsync(FullKey fullKey, AuditActor actor)
    {
        return RetryAsync(() => DeleteCoreAsync(fullKey, actor));
    }

    public async Task<AnnotationRestoreResult> RestoreAnnotationsAsync(
        string organization,
        string project,
        string view,
        IReadOnlyCollection<AnnotationKey> keys,
        AuditActor actor,
        bool includeDescendants = true)
    {
        var ordered = keys
            .DistinctBy(key => key.ToString(), StringComparer.Ordinal)
            .OrderBy(key => key.GetDepth())
            .ThenBy(key => key.ToString(), StringComparer.Ordinal)
            .ToList();

        var annotations = new List<Annotation>();
        var createdKeys = new List<string>();
        var failures = new List<AnnotationKeyFailure>();
        var handled = new HashSet<string>(StringComparer.Ordinal);

        foreach (var key in ordered)
        {
            if (handled.Contains(key.ToString()))
            {
                continue;
            }

            var fullKey = FullKey.Create(key, organization, project, view);
            var deletion = new DeletionHolder();

            try
            {
                var restored = await RetryAsync(() => RestoreCoreAsync(fullKey, actor, includeDescendants, deletion));

                foreach (var (annotation, isCreated) in restored)
                {
                    if (handled.Add(annotation.AnnotationKey))
                    {
                        annotations.Add(annotation);

                        if (isCreated)
                        {
                            createdKeys.Add(annotation.AnnotationKey);
                        }
                    }
                }

                handled.Add(key.ToString());
            }
            catch (StorytellerException exception)
            {
                failures.Add(new AnnotationKeyFailure { Key = key, ErrorCode = exception.ErrorCode, Message = exception.Message });
            }
        }

        return new AnnotationRestoreResult
        {
            Annotations = annotations,
            CreatedKeys = createdKeys,
            Failed = failures,
        };
    }

    public Task<AnnotationPurgeResult> PurgeAnnotationAsync(FullKey fullKey, AuditActor actor)
    {
        return RetryAsync(() => PurgeCoreAsync(fullKey));
    }

    public async Task<AnnotationHistory?> GetAnnotationHistoryAsync(FullKey fullKey)
    {
        var node = await Store(fullKey.OrganizationName).LoadAsync(fullKey, withConfiguration: false);

        if (node.State == AnnotationItemState.Missing)
        {
            return null;
        }

        var entries = node.Audit?.Entries ?? [];

        return new AnnotationHistory
        {
            AnnotationKey = node.KeyText,
            State = node.State == AnnotationItemState.Deleted ? AnnotationState.Deleted : AnnotationState.Active,
            Entries = entries.Reverse().ToList(),
            IsTrimmed = node.Audit?.IsTrimmed ?? false,
        };
    }

    private static int GetDeleteWave(AnnotationNode node, AnnotationNode root)
    {
        return ReferenceEquals(node, root) ? RootWave : 3 - node.Key.Annotation.GetDepth();
    }

    private async Task<AnnotationDeletionResult> DeleteCoreAsync(FullKey fullKey, AuditActor actor)
    {
        var store = Store(fullKey.OrganizationName);
        var root = await store.LoadAsync(fullKey);

        switch (root.State)
        {
            case AnnotationItemState.Missing:
                throw new NotFoundException($"The annotation '{fullKey.Annotation}' does not exist.");

            case AnnotationItemState.Deleted:
                return new AnnotationDeletionResult { Deletion = root.Entity!.ToModel().Deletion!, DeletedKeys = [] };
        }

        var descendants = await store.LoadDescendantsAsync(fullKey);
        var now = _timeProvider.GetUtcNow();

        // a delete that did not finish is completed with its own deletion, so its parts expire and restore together;
        // descendants deleted with an earlier lifetime of the root (restored or re-created without them) stay apart
        var activeSince = root.Entity!.Modified?.At ?? root.Entity.Created?.At ?? DateTimeOffset.MinValue;
        var unfinished = descendants.Values
            .Where(node => node.State == AnnotationItemState.Deleted
                           && string.Equals(node.Entity!.Deletion!.RootKey, root.KeyText, StringComparison.Ordinal)
                           && node.Entity.Deletion.DeletedAt >= activeSince)
            .Select(node => node.Entity!.Deletion!)
            .OrderByDescending(deletion => deletion.DeletedAt)
            .FirstOrDefault();

        var deletedAt = unfinished?.DeletedAt ?? now;
        var deletion = new AnnotationDeletion
        {
            DeletionId = unfinished?.DeletionId ?? Guid.CreateVersion7(now).ToString("N"),
            RootKey = root.KeyText,
            DeletedAt = deletedAt,
            DeletedBy = unfinished?.DeletedBy ?? actor,
        };
        var ttl = DeletionRetention.GetRemainingSeconds(deletedAt, _options.DeletedRetentionDays, now);

        var deleting = descendants.Values
            .Where(node => node.State == AnnotationItemState.Active)
            .OrderByDescending(node => node.Key.Annotation.GetDepth())
            .ThenBy(node => node.KeyText, StringComparer.Ordinal)
            .Append(root)
            .ToList();
        var deletingKeys = deleting.Select(node => node.KeyText).ToHashSet(StringComparer.Ordinal);
        var descendantCount = deleting.Count - 1;

        using var plan = new BatchPlan(store.Container);

        foreach (var node in deleting)
        {
            var isRoot = ReferenceEquals(node, root);
            var nodeDeletion = deletion with { IsRoot = isRoot, DescendantCount = isRoot ? descendantCount : null };
            var entity = node.Entity! with { Deletion = nodeDeletion, TimeToLiveInSeconds = ttl };
            var stream = plan.Keep(_serializer.WriteAnnotation(entity));
            var payloadBytes = stream.Length;
            var operations = new List<Action<TransactionalBatch>>
            {
                batch => batch.ReplaceItemStream(node.AnnotationId, stream, new TransactionalBatchItemRequestOptions { IfMatchEtag = node.ETag }),
            };

            if (node.Configuration is { } configuration)
            {
                // the last content survives an expiry in the versions, like after a permanent delete
                if (configuration.Content.HasValues)
                {
                    var snapshot = ToHistorySnapshot(configuration);
                    operations.Add(batch => batch.UpsertItem(snapshot));
                    payloadBytes += EstimateBytes(snapshot);
                }

                operations.Add(batch => batch.PatchItem(
                    node.ConfigurationId,
                    [
                        PatchOperation.Set($"/{nameof(Entities.Configurations.ConfigurationEntity.AnnotationDeletionId)}", deletion.DeletionId),
                        PatchOperation.Set("/ttl", ttl),
                    ]));
            }

            var entry = AnnotationAuditStore.NewEntry(AnnotationAuditAction.Deleted, actor, now) with
            {
                DeletionId = deletion.DeletionId,
                Cause = isRoot ? null : new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Cascade, Key = root.KeyText },
                DescendantCount = isRoot ? descendantCount : null,
            };
            var audit = plan.Keep(_audit.Prepare(node, [entry], ttl, out _));
            operations.Add(AnnotationAuditStore.Write(node, audit));
            plan.Add(GetDeleteWave(node, root), node.PartitionKey, payloadBytes + audit.Length, operations);
        }

        // the active parents outside of the deleted tree drop the names of their deleted children
        var removals = deleting
            .SelectMany(node => AnnotationRelations.GetParentRelations(node.Key.Annotation))
            .Where(relation => !deletingKeys.Contains(relation.Parent.ToString()))
            .GroupBy(relation => relation.Parent.ToString(), StringComparer.Ordinal)
            .ToList();
        var relations = new Dictionary<string, AnnotationNode>(StringComparer.Ordinal);
        var updates = new Dictionary<string, AnnotationEntity>(StringComparer.Ordinal);

        foreach (var group in removals)
        {
            var parent = await store.LoadAsync(FullKey.Create(group.First().Parent, fullKey), withConfiguration: false, withAudit: false);

            if (parent.State != AnnotationItemState.Active)
            {
                continue;
            }

            var entity = parent.Entity!;

            foreach (var (_, array, value) in group)
            {
                entity = AnnotationRelations.Remove(entity, array, value);
            }

            if (!ReferenceEquals(entity, parent.Entity))
            {
                relations[parent.KeyText] = parent;
                updates[parent.KeyText] = entity;
            }
        }

        AddRelationUpdates(plan, relations, updates);
        await plan.ExecuteAsync();

        return new AnnotationDeletionResult
        {
            Deletion = deletion with
            {
                IsRoot = true,
                DescendantCount = descendantCount,
                PurgeAt = deletedAt.AddDays(_options.DeletedRetentionDays),
            },
            DeletedKeys = deleting.Select(node => node.KeyText).ToList(),
        };
    }

    private async Task<IReadOnlyList<(Annotation Annotation, bool IsCreated)>> RestoreCoreAsync(
        FullKey fullKey,
        AuditActor actor,
        bool includeDescendants,
        DeletionHolder holder)
    {
        var store = Store(fullKey.OrganizationName);
        var key = fullKey.Annotation;
        var target = await store.LoadAsync(fullKey);

        if (target.State == AnnotationItemState.Missing)
        {
            throw new NotFoundException($"The annotation '{key}' does not exist or was deleted permanently.");
        }

        // a retry after a partial restore finds the target active: the first attempt remembered its deletion
        holder.DeletionId ??= target.Entity!.Deletion?.DeletionId;

        var now = _timeProvider.GetUtcNow();
        var stamp = Stamp(actor, now);
        var targetModel = target.Entity!.ToModel();
        var ancestors = await LoadNodesAsync(store, key.GetAllAncestorKeys().Select(ancestor => FullKey.Create(ancestor, fullKey)));
        var known = new Dictionary<string, AnnotationNode>(ancestors, StringComparer.Ordinal) { [target.KeyText] = target };
        var writes = new List<PendingWrite>();
        var alone = new HashSet<string>(StringComparer.Ordinal);
        var ancestorCause = new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Ancestor, Key = key };

        foreach (var ancestorKey in key.GetAllAncestorKeys())
        {
            var ancestor = ancestors[ancestorKey.ToString()];

            switch (ancestor.State)
            {
                case AnnotationItemState.Deleted:
                    writes.Add(Restoring(ancestor, actor, stamp, now, ancestorCause, descendantCount: null));
                    alone.Add(ancestor.KeyText);
                    break;

                case AnnotationItemState.Missing:
                    var model = AnnotationFactory.CreateDefault(ancestorKey, targetModel.ProjectName, targetModel.ViewName, targetModel);
                    var entry = AnnotationAuditStore.NewEntry(AnnotationAuditAction.Created, actor, now) with { Cause = ancestorCause };
                    writes.Add(new PendingWrite(ancestor, ToStoredEntity(model) with { Created = stamp, Modified = stamp }, entry, PendingKind.Create));
                    break;
            }
        }

        var descendants = includeDescendants && holder.DeletionId is not null
            ? (await store.LoadDescendantsAsync(fullKey)).Values
                .Where(node => node.State == AnnotationItemState.Deleted && node.Entity!.Deletion!.DeletionId == holder.DeletionId)
                .OrderBy(node => node.Key.Annotation.GetDepth())
                .ThenBy(node => node.KeyText, StringComparer.Ordinal)
                .ToList()
            : [];

        if (target.State == AnnotationItemState.Deleted)
        {
            writes.Add(Restoring(target, actor, stamp, now, cause: null, descendantCount: includeDescendants ? descendants.Count : null));

            if (!includeDescendants)
            {
                alone.Add(target.KeyText);
            }
        }

        var cascadeCause = new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Cascade, Key = key };
        writes.AddRange(descendants.Select(node => Restoring(node, actor, stamp, now, cascadeCause, descendantCount: null)));

        foreach (var node in descendants)
        {
            known[node.KeyText] = node;
        }

        var relations = new Dictionary<string, AnnotationNode>(StringComparer.Ordinal);
        var updates = new Dictionary<string, AnnotationEntity>(StringComparer.Ordinal);

        // an annotation restored alone keeps its deleted children deleted: they leave its arrays and become roots
        foreach (var keyText in alone)
        {
            var index = writes.FindIndex(write => write.Node.KeyText == keyText);
            var write = writes[index];
            var children = await CollectDeletedChildrenAsync(store, write.Node, writes, known, relations, updates, now);
            var entity = write.Entity;

            foreach (var child in children)
            {
                foreach (var (parent, array, value) in AnnotationRelations.GetParentRelations(child.Key.Annotation))
                {
                    if (string.Equals(parent.ToString(), keyText, StringComparison.Ordinal))
                    {
                        entity = AnnotationRelations.Remove(entity, array, value);
                    }
                }
            }

            writes[index] = write with { Entity = entity };
        }

        // a restored descendant can have an active parent outside of the restored tree (the context of an execution)
        var written = writes.Select(write => write.Node.KeyText).ToHashSet(StringComparer.Ordinal);
        var outsideParents = writes
            .SelectMany(write => AnnotationRelations.GetParentRelations(write.Node.Key.Annotation))
            .Select(relation => relation.Parent)
            .Where(parent => !written.Contains(parent.ToString()) && !known.ContainsKey(parent.ToString()))
            .DistinctBy(parent => parent.ToString(), StringComparer.Ordinal)
            .ToList();

        foreach (var parent in outsideParents)
        {
            known[parent.ToString()] = await store.LoadAsync(FullKey.Create(parent, fullKey), withConfiguration: false, withAudit: false);
        }

        AddToParents(writes, keyText => known.GetValueOrDefault(keyText), relations, updates);

        // the snapshot written by the deletion duplicates the current version and is removed; a delete of an absent
        // item fails the whole batch, so only the snapshots that still exist are deleted
        var snapshotIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var write in writes.Where(write => write.Kind == PendingKind.Restore))
        {
            if (write.Node.Configuration is { AnnotationDeletionId: not null } configuration && configuration.Content.HasValues)
            {
                var snapshotId = AnnotationItemIds.ConfigurationVersion(configuration.ViewName, configuration.AnnotationKey, configuration.Version);

                if (await store.ExistsAsync(snapshotId, write.Node.PartitionKey))
                {
                    snapshotIds.Add(snapshotId);
                }
            }
        }

        using var plan = new BatchPlan(store.Container);

        foreach (var write in writes)
        {
            var node = write.Node;
            var stream = plan.Keep(_serializer.WriteAnnotation(write.Entity));
            var operations = new List<Action<TransactionalBatch>>();

            if (write.Kind == PendingKind.Create)
            {
                operations.Add(batch => batch.CreateItemStream(stream));
            }
            else
            {
                operations.Add(batch => batch.ReplaceItemStream(node.AnnotationId, stream, new TransactionalBatchItemRequestOptions { IfMatchEtag = node.ETag }));

                if (node.Configuration is { } configuration)
                {
                    // the restored configuration is visible again and calculates its effective document anew
                    var patch = new List<PatchOperation>
                    {
                        PatchOperation.Set<object?>($"/{nameof(configuration.CalculatedContent)}", null),
                        PatchOperation.Set<object?>($"/{nameof(configuration.CalculatedContentHash)}", null),
                    };

                    if (configuration.AnnotationDeletionId is not null)
                    {
                        patch.Add(PatchOperation.Remove($"/{nameof(configuration.AnnotationDeletionId)}"));
                    }

                    if (configuration.TimeToLiveInSeconds is not null)
                    {
                        patch.Add(PatchOperation.Remove("/ttl"));
                    }

                    operations.Add(batch => batch.PatchItem(node.ConfigurationId, patch));

                    var snapshotId = AnnotationItemIds.ConfigurationVersion(configuration.ViewName, configuration.AnnotationKey, configuration.Version);

                    if (snapshotIds.Contains(snapshotId))
                    {
                        operations.Add(batch => batch.DeleteItem(snapshotId));
                    }
                }
            }

            var audit = plan.Keep(_audit.Prepare(node, [write.Entry], timeToLiveSeconds: null, out _));
            operations.Add(AnnotationAuditStore.Write(node, audit));
            plan.Add(node.Key.Annotation.GetDepth(), node.PartitionKey, stream.Length + audit.Length, operations);
        }

        AddRelationUpdates(plan, relations, updates);
        await plan.ExecuteAsync();

        return writes
            .Select(write => (write.Entity.ToModel(), write.Kind == PendingKind.Create))
            .ToList();
    }

    private PendingWrite Restoring(
        AnnotationNode node,
        AuditActor actor,
        AuditStamp stamp,
        DateTimeOffset now,
        AnnotationAuditCause? cause,
        int? descendantCount)
    {
        var entry = AnnotationAuditStore.NewEntry(AnnotationAuditAction.Restored, actor, now) with
        {
            DeletionId = node.Entity!.Deletion?.DeletionId,
            Cause = cause,
            DescendantCount = descendantCount,
        };

        return new PendingWrite(
            node,
            node.Entity with { Deletion = null, TimeToLiveInSeconds = null, Modified = stamp },
            entry,
            PendingKind.Restore);
    }

    private async Task<AnnotationPurgeResult> PurgeCoreAsync(FullKey fullKey)
    {
        var store = Store(fullKey.OrganizationName);
        var root = await store.LoadAsync(fullKey);

        switch (root.State)
        {
            case AnnotationItemState.Missing:
                throw new NotFoundException($"The annotation '{fullKey.Annotation}' does not exist.");

            case AnnotationItemState.Active:
                throw new ConflictException(
                    $"The annotation '{fullKey.Annotation}' is not deleted; delete it before deleting it permanently.",
                    ErrorCodes.AnnotationNotDeleted);
        }

        var descendants = await store.LoadDescendantsAsync(fullKey);

        if (descendants.Values.Any(node => node.State == AnnotationItemState.Active))
        {
            throw new ConflictException($"A descendant of '{fullKey.Annotation}' was restored concurrently; nothing was deleted.");
        }

        var purging = descendants.Values
            .OrderByDescending(node => node.Key.Annotation.GetDepth())
            .ThenBy(node => node.KeyText, StringComparer.Ordinal)
            .Append(root)
            .ToList();

        using var plan = new BatchPlan(store.Container);

        foreach (var node in purging)
        {
            var operations = new List<Action<TransactionalBatch>>();
            var payloadBytes = 0L;

            if (node.Configuration is { } configuration)
            {
                if (configuration.Content.HasValues)
                {
                    var snapshot = ToHistorySnapshot(configuration);
                    operations.Add(batch => batch.UpsertItem(snapshot));
                    payloadBytes += EstimateBytes(snapshot);
                }

                operations.Add(batch => batch.DeleteItem(node.ConfigurationId));
            }

            operations.Add(batch => batch.DeleteItem(node.AnnotationId, new TransactionalBatchItemRequestOptions { IfMatchEtag = node.ETag }));

            if (node.Audit is not null)
            {
                operations.Add(batch => batch.DeleteItem(node.AuditId));
            }

            plan.Add(GetDeleteWave(node, root), node.PartitionKey, payloadBytes, operations);
        }

        await plan.ExecuteAsync();

        return new AnnotationPurgeResult { PurgedKeys = purging.Select(node => node.KeyText).ToList() };
    }

    // The deletion a restore follows; kept across the retries of one key.
    private sealed class DeletionHolder
    {
        public string? DeletionId { get; set; }
    }
}
