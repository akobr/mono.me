using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Annotations;
using AutoMapper;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;

using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Annotating;

public class CosmosAnnotationService : IAnnotationService
{
    private static readonly JsonNodeOptions JsonNodeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IConfigurationService _configurationService;
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IMapper _mapper;
    private readonly JsonSerializerOptions _serializationOptions;

    public CosmosAnnotationService(
        IConfigurationService configurationService,
        IContainerRepositoryProvider repositoryProvider,
        IMapper mapper,
        IOptions<JsonSerializerOptions> serializationOptions)
    {
        _configurationService = configurationService;
        _repositoryProvider = repositoryProvider;
        _mapper = mapper;
        _serializationOptions = serializationOptions.Value;
    }

    public Task<bool> ExistAnnotationAsync(FullKey fullKey)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(fullKey.OrganizationName);
        return repository.Container.ExistsAsync(fullKey);
    }

    public async Task<Annotation?> GetAnnotationAsync(FullKey fullKey)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(fullKey.OrganizationName);
        using var response = await repository.Container.ReadItemStreamAsync(fullKey.GetCosmosItemId(), fullKey.GetCosmosPartitionKey());

        if (response.StatusCode == HttpStatusCode.NotFound
            || response.Content is null)
        {
            return null;
        }

        var jData = await JsonNode.ParseAsync(response.Content, JsonNodeOptions);

        if (jData is null
            || !jData.AsObject().TryGetPropertyValue(nameof(Annotation.AnnotationType), out var typeProperty)
            || typeProperty is null)
        {
            return null;
        }

        var annotationType = Enum.Parse<AnnotationType>(typeProperty.GetValue<string>());
        var types = CosmosTypeCodes.GetTypesPair(annotationType);
        var entity = (AnnotationEntity)jData.Deserialize(types.Entity, _serializationOptions)!;
        return (Annotation)_mapper.Map(entity, types.Entity, types.Annotation);
    }

    public async Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request)
    {
        var model = new RequestProcessModel(request);

        var processFunctionsInOrder = new Func<RequestProcessModel, Task>[]
        {
            m => GetAnnotationsAsync<Responsibility, ResponsibilityEntity>(m, AnnotationType.Responsibility, model.PartitionKey),
            m => GetAnnotationsAsync<Subject, SubjectEntity>(m, AnnotationType.Subject, model.PartitionKey),
            m => GetAnnotationsAsync<Usage, UsageEntity>(m, AnnotationType.Usage, model.PartitionKey),
            m => GetAnnotationsAsync<Context, ContextEntity>(m, AnnotationType.Context, model.PartitionKey),
            m => GetAnnotationsAsync<Execution, ExecutionEntity>(m, AnnotationType.Execution, model.PartitionKey),
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

    public async Task CreateAnnotationAsync(string organization, Annotation annotation)
    {
        var key = annotation.GetFullKey(organization);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        if (await repository.Container.ExistsAsync(key))
        {
            throw new InvalidOperationException($"Annotation with key {key.Annotation} already exist.");
        }

        switch (annotation.AnnotationType)
        {
            case AnnotationType.Responsibility:
            case AnnotationType.Subject:
            {
                await UpsertAnnotationEntityAsync(annotation, repository);
                break;
            }

            case AnnotationType.Usage:
            {
                var subjectKey = key.Annotation.GetSubjectKey();
                var subjectPartitionKey = new PartitionKey(key.GetPartitionKey(subjectKey));
                var responsibilityName = key.Annotation.GetResponsibilityName();

                await repository.Container.PatchItemAsync<SubjectEntity>(
                    key.GetCosmosItemId(subjectKey),
                    subjectPartitionKey,
                    [PatchOperation.Add($"/{nameof(SubjectEntity.Usages)}/-", responsibilityName)],
                    new PatchItemRequestOptions { EnableContentResponseOnWrite = false });
                await UpsertAnnotationEntityAsync(annotation, repository);
                break;
            }

            case AnnotationType.Context:
            {
                var subjectKey = key.Annotation.GetSubjectKey();
                var subjectPartitionKey = new PartitionKey(key.GetPartitionKey(subjectKey));
                var contextName = key.Annotation.GetContextName();
                var transaction = repository.Container.CreateTransactionalBatch(subjectPartitionKey);

                transaction.PatchItem(
                    key.GetCosmosItemId(subjectKey),
                    [PatchOperation.Add($"/{nameof(SubjectEntity.Contexts)}/-", contextName)]);

                await UpsertAnnotationEntityAsync(annotation, transaction);
                break;
            }

            case AnnotationType.Execution:
            {
                var contextKey = key.Annotation.GetContextKey();
                var contextPartitionKey = new PartitionKey(key.GetPartitionKey(contextKey));
                var responsibilityName = key.Annotation.GetResponsibilityName();
                await repository.Container.PatchItemAsync<Context>(
                    key.GetCosmosItemId(contextKey),
                    contextPartitionKey,
                    [PatchOperation.Add($"/{nameof(Context.Executions)}/-", responsibilityName)],
                    new PatchItemRequestOptions { EnableContentResponseOnWrite = false });

                var usageKey = key.Annotation.GetUsageKey();
                var usagePartitionKey = new PartitionKey(key.GetPartitionKey(usageKey));
                var contextName = key.Annotation.GetContextName();
                var transaction = repository.Container.CreateTransactionalBatch(usagePartitionKey);
                transaction.PatchItem(
                    key.GetCosmosItemId(usageKey),
                    [PatchOperation.Add($"/{nameof(UsageEntity.Executions)}/-", contextName)],
                    new TransactionalBatchPatchItemRequestOptions { EnableContentResponseOnWrite = false });
                await UpsertAnnotationEntityAsync(annotation, transaction);
                break;
            }

            case AnnotationType.Unit:
            default:
                throw new ArgumentOutOfRangeException(nameof(annotation.AnnotationType));
        }
    }

    public async Task UpdateAnnotationAsync(string organization, Annotation annotation)
    {
        var key = annotation.GetFullKey(organization);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        if (!await repository.Container.ExistsAsync(key))
        {
            throw new InvalidOperationException($"Annotation with key {key} doesn't exist.");
        }

        await UpsertAnnotationEntityAsync(annotation, repository);
    }

    public async Task DeleteAnnotationAsync(FullKey fullKey)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(fullKey.OrganizationName);
        var annotation = await GetAnnotationAsync(fullKey);

        if (annotation is null)
        {
            return;
        }

        switch (annotation.AnnotationType)
        {
            case AnnotationType.Subject:
            {
                var subject = (Subject)annotation;
                var usageTasks = new List<Task>();

                // delete usage and its executions
                foreach (var responsibilityName in subject.Usages)
                {
                    var usageKey = FullKey.Create(AnnotationKey.CreateUsage(subject.Name, responsibilityName), fullKey);
                    usageTasks.Add(
                        repository.Container.DeleteUsageAndExecutionsAsync(
                            usageKey.GetCosmosItemId(),
                            $"{usageKey.ViewName}.{AnnotationTypeCodes.Execution}.{subject.Name}.{responsibilityName}.",
                            usageKey.GetCosmosPartitionKey()));
                }

                // delete contexts and the subject
                await DeleteAnnotationEntitiesAsync(
                    subject.Contexts
                        .Select(name => FullKey.Create(AnnotationKey.CreateContext(subject.Name, name), fullKey))
                        .Concat([fullKey]),
                    fullKey.GetCosmosPartitionKey(),
                    repository);

                await Task.WhenAll(usageTasks);
                break;
            }

            case AnnotationType.Responsibility:
            {
                var queryable = repository.Container.GetItemLinqQueryable<Entity>(
                    allowSynchronousQueryExecution: true);

                // delete all usages, executions, units and unit-of-executions
                var ids = queryable
                    .Where(entity => entity.Id.StartsWith($"{fullKey.ViewName}.{AnnotationTypeCodes.Usage}.")
                                     || entity.Id.StartsWith($"{fullKey.ViewName}.{AnnotationTypeCodes.Execution}.")
                                     || entity.Id.StartsWith($"{fullKey.ViewName}.{AnnotationTypeCodes.Unit}.")
                                     || entity.Id.StartsWith($"{fullKey.ViewName}.{AnnotationTypeCodes.UnitOfExecution}."))
                    .Select(entity => entity.Id)
                    .ToList();

                var transaction = repository.Container.CreateTransactionalBatch(fullKey.GetCosmosPartitionKey());

                foreach (var id in ids)
                {
                    transaction.DeleteItem(id);
                }

                // delete the responsibility
                transaction.DeleteItem(fullKey.GetCosmosItemId());
                await transaction.ExecuteAsync();

                break;
            }

            case AnnotationType.Usage:
            {
                var usage = (Usage)annotation;
                var subjectKey = FullKey.Create(fullKey.Annotation.GetSubjectKey(), fullKey);
                await repository.Container.RemoveArrayValueAsync(subjectKey.GetCosmosItemId(), nameof(SubjectEntity.Usages), usage.ResponsibilityName, subjectKey.GetCosmosPartitionKey());

                // remove executions from contexts
                foreach (var contextName in usage.Executions)
                {
                    var contextKey = FullKey.Create(AnnotationKey.CreateContext(usage.SubjectName, contextName), fullKey);
                    await repository.Container.RemoveArrayValueAsync(contextKey.GetCosmosItemId(), nameof(ContextEntity.Executions), usage.ResponsibilityName, contextKey.GetCosmosPartitionKey());
                }

                // delete executions and the usage
                await DeleteAnnotationEntitiesAsync(
                usage.Executions
                    .Select(name => FullKey.Create(AnnotationKey.CreateExecution(usage.SubjectName, usage.ResponsibilityName, name), fullKey))
                    .Concat([fullKey]),
                fullKey.GetCosmosPartitionKey(),
                repository);

                break;
            }

            case AnnotationType.Context:
            {
                var context = (Context)annotation;
                var subjectKey = FullKey.Create(fullKey.Annotation.GetSubjectKey(), fullKey);
                await repository.Container.RemoveArrayValueAsync(subjectKey.GetCosmosItemId(), nameof(SubjectEntity.Contexts), context.Name, subjectKey.GetCosmosPartitionKey());

                // remove executions from usages
                foreach (var responsibilityName in context.Executions)
                {
                    var usageKey = FullKey.Create(AnnotationKey.CreateUsage(context.SubjectName, responsibilityName), fullKey);
                    await repository.Container.RemoveArrayValueAsync(usageKey.GetCosmosItemId(), nameof(UsageEntity.Executions), context.Name, usageKey.GetCosmosPartitionKey());
                }

                // delete executions and the context
                await DeleteAnnotationEntitiesAsync(
                    context.Executions
                        .Select(name => FullKey.Create(AnnotationKey.CreateExecution(context.SubjectName, name, context.Name), fullKey))
                        .Concat([fullKey]),
                    repository);

                break;
            }

            case AnnotationType.Execution:
            {
                var execution = (Execution)annotation;
                var usageKey = FullKey.Create(fullKey.Annotation.GetUsageKey(), fullKey);
                var contextKey = FullKey.Create(fullKey.Annotation.GetContextKey(), fullKey);

                // remove execution from usage
                await repository.Container.RemoveArrayValueAsync(usageKey.GetCosmosItemId(), nameof(UsageEntity.Executions), execution.Name, usageKey.GetCosmosPartitionKey());

                // remove execution from context
                await repository.Container.RemoveArrayValueAsync(contextKey.GetCosmosItemId(), nameof(ContextEntity.Executions), execution.ResponsibilityName, contextKey.GetCosmosPartitionKey());

                // delete the execution
                await DeleteAnnotationEntityAsync(fullKey, repository);

                break;
            }

            case AnnotationType.Unit:
            default:
            {
                throw new ArgumentOutOfRangeException(nameof(annotation.AnnotationType));
            }
        }

        await _configurationService.DeleteWithDescendantsAsync(fullKey);
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
        var mapped = results.Select(entity => _mapper.Map<TEntity, TAnnotation>(entity));
        model.Annotations = model.Annotations.Concat(mapped);
        model.TotalCount += results.Count;

        if (feed.HasMoreResults)
        {
            model.OutputContinuationToken = new ContinuationToken(annotationTypeName, results.ContinuationToken);
        }
    }

    private async Task UpsertAnnotationEntityAsync(Annotation annotation, IContainerRepository repository)
    {
        var types = CosmosTypeCodes.GetTypesPair(annotation.AnnotationType);
        var entity = (AnnotationEntity)_mapper.Map(annotation, types.Annotation, types.Entity);
        var partitionKey = PartitionKeys.GetKey(annotation);

        entity = entity with
        {
            PartitionKey = partitionKey,
        };

        // TODO: [P1] this needs to use basic serializer (all serializers needs to be united, for System.Text.Json and for Newtonsoft)
        using var memoryStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            memoryStream,
            entity,
            types.Entity,
            _serializationOptions);

        memoryStream.Seek(0, SeekOrigin.Begin);
        var streamReader = new StreamReader(memoryStream);
        var jsonText = streamReader.ReadToEnd();
        memoryStream.Seek(0, SeekOrigin.Begin);
        await repository.Container.UpsertItemStreamAsync(memoryStream, new PartitionKey(partitionKey), new ItemRequestOptions { EnableContentResponseOnWrite = false });
    }

    private async Task UpsertAnnotationEntityAsync(Annotation annotation, TransactionalBatch transaction)
    {
        var types = CosmosTypeCodes.GetTypesPair(annotation.AnnotationType);
        var entity = (AnnotationEntity)_mapper.Map(annotation, types.Annotation, types.Entity);
        var partitionKey = PartitionKeys.GetKey(annotation);

        entity = entity with
        {
            PartitionKey = partitionKey,
        };

        using var memoryStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            memoryStream,
            entity,
            types.Entity,
            _serializationOptions);

        memoryStream.Seek(0, SeekOrigin.Begin);
        transaction.UpsertItemStream(memoryStream);
        await transaction.ExecuteAsync();
    }

    private static Task DeleteAnnotationEntityAsync(FullKey fullKey, IContainerRepository repository)
    {
        return repository.Container.DeleteItemStreamAsync(
            fullKey.GetCosmosItemId(),
            fullKey.GetCosmosPartitionKey(),
            new ItemRequestOptions { EnableContentResponseOnWrite = false });
    }

    private static Task DeleteAnnotationEntitiesAsync(IEnumerable<FullKey> keys, PartitionKey partitionKey, IContainerRepository repository)
    {
        var transaction = repository.Container.CreateTransactionalBatch(partitionKey);

        foreach (var key in keys)
        {
            transaction.DeleteItem(key.GetCosmosItemId());
        }

        return transaction.ExecuteAsync();
    }

    private static Task DeleteAnnotationEntitiesAsync(IEnumerable<FullKey> keys, IContainerRepository repository)
    {
        var transactionTasks = new List<Task>();

        foreach (var partitionGroup in keys.GroupBy(key => key.GetPartitionKey()))
        {
            var partitionKey = new PartitionKey(partitionGroup.Key);
            var transaction = repository.Container.CreateTransactionalBatch(partitionKey);

            foreach (var key in partitionGroup)
            {
                transaction.DeleteItem(key.GetCosmosItemId());
            }

            transactionTasks.Add(transaction.ExecuteAsync());
        }

        return Task.WhenAll(transactionTasks);
    }
}
