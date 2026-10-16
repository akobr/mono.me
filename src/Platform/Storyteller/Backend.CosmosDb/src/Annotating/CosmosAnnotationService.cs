using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Annotations;
using _42.Platform.Storyteller.Entities.Configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Annotating;

// Annotations in the organization container. Every write records its audit entry in the same transactional batch,
// and a soft-deleted annotation is invisible to every read except the reads of deleted annotations.
public partial class CosmosAnnotationService : IAnnotationService
{
    private const int MaxAttempts = 3;

    // waves of a batch plan (see BatchPlan): the relation arrays of other annotations are written last
    private const int RelationsWave = 10;

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly AnnotationItemSerializer _serializer;
    private readonly AnnotationAuditStore _audit;
    private readonly AnnotationOptions _options;
    private readonly TimeProvider _timeProvider;

    public CosmosAnnotationService(
        IContainerRepositoryProvider repositoryProvider,
        AnnotationItemSerializer serializer,
        AnnotationAuditStore audit,
        IOptions<AnnotationOptions> options,
        TimeProvider timeProvider)
    {
        _repositoryProvider = repositoryProvider;
        _serializer = serializer;
        _audit = audit;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    private enum PendingKind
    {
        Create = 0,
        Restore,
    }

    public async Task<bool> ExistAnnotationAsync(FullKey fullKey)
    {
        var node = await Store(fullKey.OrganizationName).LoadAsync(fullKey, withConfiguration: false, withAudit: false);
        return node.State == AnnotationItemState.Active;
    }

    public async Task<Annotation?> GetAnnotationAsync(FullKey fullKey)
    {
        var node = await Store(fullKey.OrganizationName).LoadAsync(fullKey, withConfiguration: false, withAudit: false);
        return node.State == AnnotationItemState.Active ? node.Entity!.ToModel() : null;
    }

    public async Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request)
    {
        var model = new RequestProcessModel(request) { Now = _timeProvider.GetUtcNow() };

        var processFunctionsInOrder = new Func<RequestProcessModel, Task>[]
        {
            m => GetAnnotationsAsync<Responsibility, ResponsibilityEntity>(m, AnnotationType.Responsibility, model.PartitionKey),
            m => GetAnnotationsAsync<Subject, SubjectEntity>(m, AnnotationType.Subject, model.PartitionKey),
            m => GetAnnotationsAsync<Usage, UsageEntity>(m, AnnotationType.Usage, model.PartitionKey),
            m => GetAnnotationsAsync<Context, ContextEntity>(m, AnnotationType.Context, model.PartitionKey),
            m => GetAnnotationsAsync<Execution, ExecutionEntity>(m, AnnotationType.Execution, model.PartitionKey),
            m => GetAnnotationsAsync<Unit, UnitEntity>(m, AnnotationType.Unit, model.PartitionKey),
            m => GetAnnotationsAsync<UnitOfExecution, UnitOfExecutionEntity>(m, AnnotationType.UnitOfExecution, model.PartitionKey),
        };

        foreach (var processFunction in processFunctionsInOrder)
        {
            await processFunction(model);

            if (model.OutputContinuationToken is not null)
            {
                break;
            }
        }

        return new AnnotationsResponse
        {
            ContinuationToken = model.OutputContinuationToken?.ToString(),
            Annotations = model.Annotations,
            Count = model.TotalCount,
        };
    }

    public async Task<IReadOnlyList<Annotation>> GetViewAnnotationsAsync(
        string organization,
        string project,
        string view,
        CancellationToken cancellationToken = default)
    {
        var annotations = new List<Annotation>();
        string? continuationToken = null;

        // a disabled, scheduled or expired annotation is exported as well: only a deleted one is not in the view
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await GetAnnotationsAsync(new AnnotationsRequest
            {
                Organization = organization,
                Project = project,
                View = view,
                Types = Enum.GetValues<AnnotationType>(),
                IncludeInactive = true,
                ContinuationToken = continuationToken,
            });

            annotations.AddRange(page.Annotations);
            continuationToken = page.ContinuationToken;
        }
        while (continuationToken is not null);

        return annotations;
    }

    public Task<Annotation> SetAnnotationAsync(string organization, Annotation annotation, AuditActor actor)
    {
        AnnotationRules.Validate(annotation);
        var fullKey = annotation.GetFullKey(organization);

        return RetryAsync(async () =>
        {
            var store = Store(organization);
            var node = await store.LoadAsync(fullKey, withConfiguration: false, withAudit: false);

            if (node.State == AnnotationItemState.Active)
            {
                await store.LoadDetailsAsync(node, withConfiguration: false, withAudit: true);
                return await UpdateCoreAsync(store, node, annotation, actor);
            }

            var created = await CreateCoreAsync(store, fullKey, annotation, actor);
            return created[^1];
        });
    }

    public async Task<IEnumerable<Annotation>> CreateAnnotationAsync(string organization, Annotation annotation, AuditActor actor)
    {
        AnnotationRules.Validate(annotation);
        var fullKey = annotation.GetFullKey(organization);
        return await RetryAsync(() => CreateCoreAsync(Store(organization), fullKey, annotation, actor));
    }

    public async Task UpdateAnnotationAsync(string organization, Annotation annotation, AuditActor actor)
    {
        AnnotationRules.Validate(annotation);
        var fullKey = annotation.GetFullKey(organization);
        await RetryAsync(async () =>
        {
            var store = Store(organization);
            return await UpdateCoreAsync(store, await store.LoadAsync(fullKey, withConfiguration: false), annotation, actor);
        });
    }

    public async Task<IEnumerable<Annotation>> CreateAnnotationsAsync(string organization, IEnumerable<Annotation> annotations, AuditActor actor)
    {
        var annotationsInOrder = annotations
            .OrderBy(a => a.AnnotationType)
            .ToList();

        AnnotationRules.ValidateAll(annotationsInOrder);

        var createdAnnotations = new List<Annotation>();
        var handled = new HashSet<string>(StringComparer.Ordinal);

        foreach (var annotation in annotationsInOrder)
        {
            var fullKey = annotation.GetFullKey(organization);

            if (!handled.Add(fullKey.ToString()) || await ExistAnnotationAsync(fullKey))
            {
                continue;
            }

            IReadOnlyList<Annotation> created;

            try
            {
                created = await RetryAsync(() => CreateCoreAsync(Store(organization), fullKey, annotation, actor));
            }
            catch (ConflictException)
            {
                // created concurrently: a bulk create skips existing keys; retries exhausted by contention fail the request
                if (!await ExistAnnotationAsync(fullKey))
                {
                    throw;
                }

                continue;
            }

            foreach (var createdAnnotation in created)
            {
                handled.Add(createdAnnotation.GetFullKey(organization).ToString());
                createdAnnotations.Add(createdAnnotation);
            }
        }

        return createdAnnotations;
    }

    public async Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, string project, string view, IEnumerable<string> annotations, AuditActor actor)
    {
        var annotationsToProcess = new List<Annotation>();

        foreach (var input in annotations)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            var segments = input.Split(Constants.KeySeparators, StringSplitOptions.RemoveEmptyEntries);
            AnnotationKey? annotationKey = null;

            if (segments.Length > 0
                && segments[0].Length == 3
                && AnnotationTypeCodes.ValidCodes.ContainsKey(segments[0]))
            {
                if (AnnotationKey.TryParse(input, out var parsedKey))
                {
                    annotationKey = parsedKey;
                }
            }

            annotationKey ??= segments.Length switch
            {
                1 => AnnotationKey.CreateSubject(segments[0]),
                2 => AnnotationKey.CreateUsage(segments[0], segments[1]),
                3 => AnnotationKey.CreateExecution(segments[0], segments[1], segments[2]),
                4 => AnnotationKey.CreateUnitOfExecution(segments[0], segments[1], segments[2], segments[3]),
                _ => null,
            };

            if (annotationKey is null)
            {
                continue;
            }

            annotationsToProcess.Add(AnnotationFactory.CreateDefault(annotationKey, project, view));
        }

        return await CreateAnnotationsAsync(organization, annotationsToProcess, actor);
    }

    private static async Task<T> RetryAsync<T>(Func<Task<T>> operation)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (AnnotationConcurrencyException exception)
            {
                if (attempt >= MaxAttempts)
                {
                    throw new ConflictException($"The annotations changed concurrently; try again. {exception.Message}");
                }
            }
        }
    }

    private static AnnotationEntity ToStoredEntity(Annotation model)
    {
        return model.ToEntity() with { PartitionKey = PartitionKeys.GetKey(model) };
    }

    private static ConfigurationHistoryEntity ToHistorySnapshot(ConfigurationEntity configuration)
    {
        return new ConfigurationHistoryEntity
        {
            PartitionKey = configuration.PartitionKey,
            Id = AnnotationItemIds.ConfigurationVersion(configuration.ViewName, configuration.AnnotationKey, configuration.Version),
            AnnotationKey = configuration.AnnotationKey,
            Version = configuration.Version,
            Name = configuration.Name,
            ProjectName = configuration.ProjectName,
            ViewName = configuration.ViewName,
            Content = configuration.Content,
            Modified = configuration.Modified,
        };
    }

    // The size of a configuration version in a batch; its content dominates, the rest is a small allowance.
    private static long EstimateBytes(ConfigurationHistoryEntity snapshot)
    {
        return Encoding.UTF8.GetByteCount(snapshot.Content.ToString(Formatting.None)) + 1024;
    }

    private static AuditStamp Stamp(AuditActor actor, DateTimeOffset now)
    {
        return new AuditStamp { At = now, By = actor };
    }

    private static bool IsSameKey(AnnotationKey left, AnnotationKey right)
    {
        return string.Equals(left.ToString(), right.ToString(), StringComparison.Ordinal);
    }

    private AnnotationItemStore Store(string organization)
    {
        return new AnnotationItemStore(_repositoryProvider.GetOrganizationContainer(organization).Container, _serializer);
    }

    private async Task<Dictionary<string, AnnotationNode>> LoadNodesAsync(AnnotationItemStore store, IEnumerable<FullKey> keys)
    {
        var nodes = await Task.WhenAll(keys.Select(key => store.LoadAsync(key)));
        return nodes.ToDictionary(node => node.KeyText, StringComparer.Ordinal);
    }

    // Creates the annotation and its ancestors that are not active. A missing key is created; a deleted one is
    // re-created: its item is replaced, its configuration goes to the versions, and its audit continues.
    private async Task<IReadOnlyList<Annotation>> CreateCoreAsync(AnnotationItemStore store, FullKey fullKey, Annotation input, AuditActor actor)
    {
        var key = fullKey.Annotation;
        var target = AnnotationFactory.Normalize(input, key);
        var now = _timeProvider.GetUtcNow();
        var stamp = Stamp(actor, now);
        var keys = key.GetAllAncestorKeys().Append(key).ToList();

        // the annotation items decide what is written; an active ancestor needs nothing more than its item
        var loaded = await Task.WhenAll(keys.Select(k => store.LoadAsync(FullKey.Create(k, fullKey), withConfiguration: false, withAudit: false)));
        var nodes = loaded.ToDictionary(node => node.KeyText, StringComparer.Ordinal);

        if (nodes[key.ToString()].State == AnnotationItemState.Active)
        {
            throw new ConflictException($"The annotation '{key}' already exists.");
        }

        // a missing key may still have an audit (it continues); a deleted one also has a configuration to retire
        await Task.WhenAll(loaded
            .Where(node => node.State != AnnotationItemState.Active)
            .Select(node => store.LoadDetailsAsync(node, withConfiguration: node.State == AnnotationItemState.Deleted, withAudit: true)));

        var writes = new List<PendingWrite>();

        foreach (var nodeKey in keys)
        {
            var node = nodes[nodeKey.ToString()];

            if (node.State == AnnotationItemState.Active)
            {
                continue;
            }

            var isTarget = IsSameKey(nodeKey, key);
            var model = isTarget
                ? target
                : AnnotationFactory.CreateDefault(nodeKey, target.ProjectName, target.ViewName, target);
            var entry = AnnotationAuditStore.NewEntry(
                node.State == AnnotationItemState.Deleted ? AnnotationAuditAction.Recreated : AnnotationAuditAction.Created,
                actor,
                now) with
            {
                Cause = isTarget ? null : new AnnotationAuditCause { Kind = AnnotationAuditCauseKind.Ancestor, Key = key },
                DeletionId = node.Entity?.Deletion?.DeletionId,
            };

            writes.Add(new PendingWrite(node, ToStoredEntity(model) with { Created = stamp, Modified = stamp }, entry, PendingKind.Create));
        }

        var relations = new Dictionary<string, AnnotationNode>(StringComparer.Ordinal);
        var updates = new Dictionary<string, AnnotationEntity>(StringComparer.Ordinal);
        AddToParents(writes, text => nodes.GetValueOrDefault(text), relations, updates);

        // the children deleted with a re-created annotation stay deleted and become roots of Deleted
        foreach (var write in writes.Where(w => w.Node.State == AnnotationItemState.Deleted))
        {
            await CollectDeletedChildrenAsync(store, write.Node, writes, nodes, relations, updates, now);
        }

        using var plan = new BatchPlan(store.Container);

        foreach (var write in writes)
        {
            var node = write.Node;
            var operations = new List<Action<TransactionalBatch>>();
            var stream = plan.Keep(_serializer.WriteAnnotation(write.Entity));
            var payloadBytes = stream.Length;

            if (node.State == AnnotationItemState.Missing)
            {
                operations.Add(batch => batch.CreateItemStream(stream));
            }
            else
            {
                operations.Add(batch => batch.ReplaceItemStream(node.AnnotationId, stream, new TransactionalBatchItemRequestOptions { IfMatchEtag = node.ETag }));

                // a re-created annotation starts without configuration; the old content stays in the versions
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
            }

            var audit = plan.Keep(_audit.Prepare(node, [write.Entry], timeToLiveSeconds: null, out _));
            operations.Add(AnnotationAuditStore.Write(node, audit));
            plan.Add(node.Key.Annotation.GetDepth(), node.PartitionKey, payloadBytes + audit.Length, operations);
        }

        AddRelationUpdates(plan, relations, updates);
        await plan.ExecuteAsync();

        return writes.Select(write => write.Entity.ToModel()).ToList();
    }

    // The node holds the annotation and its audit, read with their ETags.
    private async Task<Annotation> UpdateCoreAsync(AnnotationItemStore store, AnnotationNode node, Annotation input, AuditActor actor)
    {
        var fullKey = node.Key;

        if (node.State != AnnotationItemState.Active)
        {
            throw new NotFoundException($"The annotation '{fullKey.Annotation}' does not exist.");
        }

        var before = node.Entity!.ToModel();
        var after = AnnotationFactory.Normalize(input, fullKey.Annotation);
        var changes = AnnotationAuditDiff.Compute(before, after, _audit.MaxValueLength);

        if (changes.IsEmpty)
        {
            return before;
        }

        var now = _timeProvider.GetUtcNow();
        var stored = node.Entity!;
        var entity = AnnotationRelations.CopyFrom(ToStoredEntity(after), stored) with
        {
            Created = stored.Created,
            Modified = Stamp(actor, now),
        };
        var entry = AnnotationAuditStore.NewEntry(AnnotationAuditAction.Updated, actor, now) with
        {
            Fields = changes.Fields.Count > 0 ? changes.Fields : null,
            Labels = changes.Labels,
            Values = changes.Values.Count > 0 ? changes.Values : null,
        };

        using var plan = new BatchPlan(store.Container);
        var stream = plan.Keep(_serializer.WriteAnnotation(entity));
        var audit = plan.Keep(_audit.Prepare(node, [entry], timeToLiveSeconds: null, out _));
        plan.Add(
            0,
            node.PartitionKey,
            stream.Length + audit.Length,
            batch => batch.ReplaceItemStream(node.AnnotationId, stream, new TransactionalBatchItemRequestOptions { IfMatchEtag = node.ETag }),
            AnnotationAuditStore.Write(node, audit));
        await plan.ExecuteAsync();

        return entity.ToModel();
    }

    // Every written annotation joins the arrays of its parents. A parent written in the same operation gets the name
    // in memory; an active parent outside of it is updated with its ETag.
    private static void AddToParents(
        List<PendingWrite> writes,
        Func<string, AnnotationNode?> findNode,
        Dictionary<string, AnnotationNode> relations,
        Dictionary<string, AnnotationEntity> updates)
    {
        var written = writes.ToDictionary(write => write.Node.KeyText, StringComparer.Ordinal);

        foreach (var write in writes.ToList())
        {
            foreach (var (parent, array, value) in AnnotationRelations.GetParentRelations(write.Node.Key.Annotation))
            {
                var parentText = parent.ToString();

                if (written.TryGetValue(parentText, out var parentWrite))
                {
                    var index = writes.IndexOf(parentWrite);
                    parentWrite = parentWrite with { Entity = AnnotationRelations.Add(parentWrite.Entity, array, value) };
                    writes[index] = parentWrite;
                    written[parentText] = parentWrite;
                    continue;
                }

                var parentNode = findNode(parentText);

                if (parentNode?.State != AnnotationItemState.Active)
                {
                    continue;
                }

                var current = updates.GetValueOrDefault(parentText) ?? parentNode.Entity!;
                var updated = AnnotationRelations.Add(current, array, value);

                if (!ReferenceEquals(updated, current))
                {
                    relations[parentText] = parentNode;
                    updates[parentText] = updated;
                }
            }
        }
    }

    // Replaces the active annotations whose arrays (or root flag) changed, each guarded by its ETag.
    private void AddRelationUpdates(BatchPlan plan, Dictionary<string, AnnotationNode> nodes, Dictionary<string, AnnotationEntity> updates)
    {
        foreach (var (keyText, entity) in updates)
        {
            var node = nodes[keyText];
            var stream = plan.Keep(_serializer.WriteAnnotation(entity));
            plan.Add(
                RelationsWave,
                node.PartitionKey,
                stream.Length,
                batch => batch.ReplaceItemStream(node.AnnotationId, stream, new TransactionalBatchItemRequestOptions { IfMatchEtag = node.ETag }));
        }
    }

    // The children deleted together with a deleted annotation that becomes active alone (re-created, or restored
    // without descendants). They stay deleted: they leave its arrays, and they become roots of Deleted when no other
    // parent of theirs stays deleted. Returns them.
    private async Task<IReadOnlyList<AnnotationNode>> CollectDeletedChildrenAsync(
        AnnotationItemStore store,
        AnnotationNode node,
        List<PendingWrite> writes,
        Dictionary<string, AnnotationNode> known,
        Dictionary<string, AnnotationNode> relations,
        Dictionary<string, AnnotationEntity> updates,
        DateTimeOffset now)
    {
        var deletionId = node.Entity?.Deletion?.DeletionId;

        if (deletionId is null)
        {
            return [];
        }

        var key = node.Key.Annotation;
        var descendants = await store.LoadDescendantsAsync(node.Key);
        var written = writes.Select(write => write.Node.KeyText).ToHashSet(StringComparer.Ordinal);
        var children = descendants.Values
            .Where(child => child.State == AnnotationItemState.Deleted
                            && child.Entity!.Deletion!.DeletionId == deletionId
                            && !written.Contains(child.KeyText)
                            && child.Key.Annotation.GetDirectAncestorKeys().Any(parent => IsSameKey(parent, key)))
            .ToList();

        foreach (var child in children)
        {
            var deletion = child.Entity!.Deletion!;

            if (deletion.IsRoot)
            {
                continue;
            }

            var otherParents = child.Key.Annotation.GetDirectAncestorKeys().Where(parent => !IsSameKey(parent, key));
            var isRoot = true;

            foreach (var parent in otherParents)
            {
                var parentText = parent.ToString();

                if (written.Contains(parentText))
                {
                    continue;
                }

                var parentNode = known.GetValueOrDefault(parentText)
                                 ?? descendants.GetValueOrDefault(parentText)
                                 ?? await store.LoadAsync(FullKey.Create(parent, node.Key), withConfiguration: false, withAudit: false);

                if (parentNode.State != AnnotationItemState.Active)
                {
                    isRoot = false;
                    break;
                }
            }

            if (isRoot)
            {
                relations[child.KeyText] = child;
                updates[child.KeyText] = child.Entity with
                {
                    Deletion = deletion with { IsRoot = true },
                    TimeToLiveInSeconds = DeletionRetention.GetRemainingSeconds(deletion.DeletedAt, _options.DeletedRetentionDays, now),
                };
            }
        }

        return children;
    }

    private async Task GetAnnotationsAsync<TAnnotation, TEntity>(RequestProcessModel model, AnnotationType annotationType, PartitionKey? partitionKey = null)
        where TAnnotation : Annotation
        where TEntity : AnnotationEntity
    {
        var annotationTypeName = Enum.GetName(annotationType) ?? "Unknown";

        if (!model.TypesMap.Contains(annotationType)
            || model.InputContinuationToken is not null && model.InputContinuationToken.Type != annotationTypeName)
        {
            return;
        }

        model.InputContinuationToken = null;

        if (model.TotalCount >= CosmosConstants.MaxItemCountPerPage)
        {
            model.OutputContinuationToken = ContinuationToken.CreateFirstPage(annotationTypeName);
            return;
        }

        var dynamicConditions = model.Request.Conditions
            .Where(b => b.Against == annotationType)
            .Cast<AnnotationsRequest.Condition<TEntity>>();

        // descendants of a key: the scope gives the partition and the key prefix of this type; the prefix can cover
        // more (the units of execution of every unit of a responsibility), so the page is filtered by the graph
        var descendantsOf = model.Request.DescendantsOf;
        string? keyPrefix = null;
        string? partitionPrefix = null;

        if (descendantsOf is not null)
        {
            var typeCode = AnnotationTypeCodes.ValidCodes.First(pair => pair.Value == annotationType).Key;
            var root = FullKey.Create(descendantsOf, model.Request.Organization, model.Request.Project, model.Request.View);
            var scope = DescendantScope.Get(root)
                .Select(candidate => (Scope: candidate, Prefix: candidate.KeyPrefixes.FirstOrDefault(prefix => prefix.StartsWith($"{typeCode}.", StringComparison.Ordinal))))
                .FirstOrDefault(candidate => candidate.Prefix is not null);

            if (scope.Prefix is null)
            {
                return;
            }

            keyPrefix = scope.Prefix;
            partitionKey = scope.Scope.PartitionKey is null ? null : new PartitionKey(scope.Scope.PartitionKey);
            partitionPrefix = scope.Scope.PartitionKey is null ? scope.Scope.PartitionPrefix : null;
        }

        var repository = _repositoryProvider.GetOrganizationContainer(model.Request.Organization);
        var queryable = repository.Container.GetItemLinqQueryable<TEntity>(
            continuationToken: model.InputContinuationToken?.CosmosToken,
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = partitionKey,
                MaxItemCount = CosmosConstants.MaxItemCountPerPage,
            });

        var matches = queryable
            .Where(r => r.AnnotationType == annotationType)
            .Where(r => r.ProjectName == model.Request.Project)
            .Where(r => r.ViewName == model.Request.View);

        // an absent Deletion is undefined in Cosmos, never null: the filters use IS_DEFINED
        matches = model.Request.Deleted switch
        {
            AnnotationDeletedFilter.Exclude => matches.Where(r => !r.Deletion.IsDefined()),
            AnnotationDeletedFilter.Only => matches.Where(r => r.Deletion.IsDefined()),
            _ => matches,
        };

        // an undefined IsDisabled fails every comparison in Cosmos, so "!= true" alone would drop it; the validity
        // window is checked on the page below: the dates keep the offset they were written with, so Cosmos can't
        // compare them as strings
        if (!model.Request.IncludeInactive)
        {
            matches = matches.Where(r => !r.IsDisabled.IsDefined() || r.IsDisabled == null || r.IsDisabled == false);
        }

        if (model.Request.DeletionRootsOnly)
        {
            matches = matches.Where(r => r.Deletion!.IsRoot);
        }

        if (!string.IsNullOrEmpty(model.Request.DeletionId))
        {
            var deletionId = model.Request.DeletionId;
            matches = matches.Where(r => r.Deletion!.DeletionId == deletionId);
        }

        if (keyPrefix is not null)
        {
            matches = matches.Where(r => r.AnnotationKey.StartsWith(keyPrefix));
        }

        if (partitionPrefix is not null)
        {
            matches = matches.Where(r => r.PartitionKey.StartsWith(partitionPrefix));
        }

        foreach (var by in dynamicConditions)
        {
            matches = matches.Where(by.Predicate);
        }

        var feed = matches.ToFeedIterator();

        if (!feed.HasMoreResults)
        {
            return;
        }

        var results = await feed.ReadNextAsync();
        var mapped = results
            .Where(entity => descendantsOf is null || (AnnotationKey.TryParse(entity.AnnotationKey, out var key) && key.IsDescendantOf(descendantsOf)))
            .Where(entity => model.Request.IncludeInactive
                             || AnnotationStatusExtensions.GetStatus(entity.IsDisabled, entity.ValidFrom, entity.ExpiresAt, model.Now) == AnnotationStatus.Active)
            .Select(entity => entity.ToModel<TEntity, TAnnotation>())
            .ToList();
        model.Annotations = model.Annotations.Concat(mapped);
        model.TotalCount += mapped.Count;

        if (feed.HasMoreResults)
        {
            model.OutputContinuationToken = new ContinuationToken(annotationTypeName, results.ContinuationToken);
        }
    }

    // An annotation the operation writes, with its new entity and audit entry.
    private sealed record PendingWrite(AnnotationNode Node, AnnotationEntity Entity, AnnotationAuditEntry Entry, PendingKind Kind);
}
