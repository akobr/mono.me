using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

internal static class ConfigurationCacheInvalidator
{
    // Cosmos DB limit of operations in one transactional batch
    public const int MaxBatchOperations = 100;

    private const int MaxParallelBatches = 8;

    // Configuration types which merge the template of the key type (the type itself and all its descendants, see BuildInheritanceGraph)
    private static readonly IReadOnlyDictionary<string, string[]> TemplateAffectedTypeCodes
        = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            {
                AnnotationTypeCodes.Responsibility,
                [AnnotationTypeCodes.Responsibility, AnnotationTypeCodes.Unit, AnnotationTypeCodes.Usage, AnnotationTypeCodes.Execution, AnnotationTypeCodes.UnitOfExecution]
            },
            { AnnotationTypeCodes.Unit, [AnnotationTypeCodes.Unit, AnnotationTypeCodes.UnitOfExecution] },
            {
                AnnotationTypeCodes.Subject,
                [AnnotationTypeCodes.Subject, AnnotationTypeCodes.Usage, AnnotationTypeCodes.Context, AnnotationTypeCodes.Execution, AnnotationTypeCodes.UnitOfExecution]
            },
            { AnnotationTypeCodes.Usage, [AnnotationTypeCodes.Usage, AnnotationTypeCodes.Execution, AnnotationTypeCodes.UnitOfExecution] },
            { AnnotationTypeCodes.Context, [AnnotationTypeCodes.Context, AnnotationTypeCodes.Execution, AnnotationTypeCodes.UnitOfExecution] },
            { AnnotationTypeCodes.Execution, [AnnotationTypeCodes.Execution, AnnotationTypeCodes.UnitOfExecution] },
            { AnnotationTypeCodes.UnitOfExecution, [AnnotationTypeCodes.UnitOfExecution] },
        };

    // Set to null instead of remove, a remove of a missing property fails the whole batch
    private static readonly IReadOnlyList<PatchOperation> InvalidationPatch =
    [
        PatchOperation.Set<object?>($"/{nameof(ConfigurationEntity.CalculatedContent)}", null),
        PatchOperation.Set<object?>($"/{nameof(ConfigurationEntity.CalculatedContentHash)}", null),
        PatchOperation.Increment($"/{nameof(ConfigurationEntity.AffectedCounter)}", 1),
    ];

    public static IReadOnlyCollection<string> GetTemplateAffectedTypeCodes(string typeCode)
    {
        return TemplateAffectedTypeCodes.TryGetValue(typeCode, out var codes)
            ? codes
            : throw new ArgumentOutOfRangeException(nameof(typeCode), typeCode, "Unknown annotation type code.");
    }

    /// <summary>
    /// Invalidates cached calculations of all configurations in the view which merge the view template of the given type.
    /// </summary>
    public static Task InvalidateForTemplateAsync(IContainerRepository repository, string projectName, string viewName, string typeCode)
    {
        var predicate = BuildTemplatePredicate(projectName, viewName, GetTemplateAffectedTypeCodes(typeCode));
        return InvalidateAsync(repository, predicate);
    }

    public static async Task InvalidateAsync(
        IContainerRepository repository,
        Expression<Func<ConfigurationEntity, bool>> predicate)
    {
        var feed = repository.Container.GetItemLinqQueryable<ConfigurationEntity>(
                requestOptions: new QueryRequestOptions { MaxItemCount = CosmosConstants.MaxItemCountPerPage })
            .Where(predicate)
            .Select(config => new EntityIndices { PartitionKey = config.PartitionKey, Id = config.Id })
            .ToFeedIterator();

        var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (feed.HasMoreResults)
        {
            foreach (var indices in await feed.ReadNextAsync())
            {
                AddToGroup(groups, indices.PartitionKey, indices.Id);
            }
        }

        await PatchAsync(repository, groups);
    }

    public static async Task InvalidateAsync(
        IContainerRepository repository,
        Expression<Func<ConfigurationEntity, bool>> predicate,
        PartitionKey partitionKey)
    {
        var feed = repository.Container.GetItemLinqQueryable<ConfigurationEntity>(
                requestOptions: new QueryRequestOptions
                {
                    PartitionKey = partitionKey,
                    MaxItemCount = CosmosConstants.MaxItemCountPerPage,
                })
            .Where(predicate)
            .Select(config => config.Id)
            .ToFeedIterator();

        var ids = new List<string>();
        while (feed.HasMoreResults)
        {
            ids.AddRange(await feed.ReadNextAsync());
        }

        await PatchAsync(repository, [(partitionKey, ids)]);
    }

    private static Task PatchAsync(IContainerRepository repository, Dictionary<string, List<string>> groups)
    {
        return PatchAsync(
            repository,
            groups.Select(group => (new PartitionKey(group.Key), (IReadOnlyList<string>)group.Value)));
    }

    private static async Task PatchAsync(
        IContainerRepository repository,
        IEnumerable<(PartitionKey PartitionKey, IReadOnlyList<string> Ids)> groups)
    {
        using var throttler = new SemaphoreSlim(MaxParallelBatches);
        var tasks = new List<Task>();

        foreach (var (partitionKey, ids) in groups)
        {
            foreach (var chunk in ids.Chunk(MaxBatchOperations))
            {
                tasks.Add(PatchChunkAsync(repository.Container, partitionKey, chunk, throttler));
            }
        }

        await Task.WhenAll(tasks);
    }

    private static async Task PatchChunkAsync(Container container, PartitionKey partitionKey, string[] ids, SemaphoreSlim throttler)
    {
        await throttler.WaitAsync();

        try
        {
            var batch = container.CreateTransactionalBatch(partitionKey);
            foreach (var id in ids)
            {
                batch.PatchItem(id, InvalidationPatch);
            }

            using var response = await batch.ExecuteAsync();
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            if (!response.Any(result => result.StatusCode == HttpStatusCode.NotFound))
            {
                throw new InvalidOperationException(
                    $"Invalidation of cached configurations failed in partition {partitionKey} with status {response.StatusCode}: {response.ErrorMessage}");
            }

            // an item has been deleted meanwhile, fall back to one-by-one patching (deleted items are skipped)
            foreach (var id in ids)
            {
                using var itemResponse = await container.PatchItemStreamAsync(id, partitionKey, InvalidationPatch);
                if (!itemResponse.IsSuccessStatusCode && itemResponse.StatusCode != HttpStatusCode.NotFound)
                {
                    throw new InvalidOperationException(
                        $"Invalidation of cached configuration '{id}' failed with status {itemResponse.StatusCode}: {itemResponse.ErrorMessage}");
                }
            }
        }
        finally
        {
            throttler.Release();
        }
    }

    private static void AddToGroup(Dictionary<string, List<string>> groups, string partitionKey, string id)
    {
        if (!groups.TryGetValue(partitionKey, out var ids))
        {
            ids = [];
            groups[partitionKey] = ids;
        }

        ids.Add(id);
    }

    private static Expression<Func<ConfigurationEntity, bool>> BuildTemplatePredicate(string projectName, string viewName, IEnumerable<string> typeCodes)
    {
        var config = Expression.Parameter(typeof(ConfigurationEntity), "config");
        var startsWith = typeof(string).GetMethod(nameof(string.StartsWith), [typeof(string)])!;
        var annotationKey = Expression.Property(config, nameof(Entity.AnnotationKey));

        Expression? anyType = null;
        foreach (var typeCode in typeCodes)
        {
            var isType = Expression.Call(annotationKey, startsWith, Expression.Constant($"{typeCode}."));
            anyType = anyType is null ? isType : Expression.OrElse(anyType, isType);
        }

        // only configuration items of the view ({view}.cnf.{annotationKey}), never history or annotation items
        var body = Expression.AndAlso(
            Expression.AndAlso(
                Expression.AndAlso(
                    Expression.Equal(Expression.Property(config, nameof(Entity.ProjectName)), Expression.Constant(projectName)),
                    Expression.Equal(Expression.Property(config, nameof(Entity.ViewName)), Expression.Constant(viewName))),
                Expression.Call(Expression.Property(config, nameof(Entity.Id)), startsWith, Expression.Constant($"{viewName}.{EntityIdPrefixTypes.Configuration}."))),
            anyType ?? Expression.Constant(false));

        return Expression.Lambda<Func<ConfigurationEntity, bool>>(body, config);
    }
}
