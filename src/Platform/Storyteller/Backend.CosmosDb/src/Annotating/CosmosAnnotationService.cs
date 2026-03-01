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

    public async Task<IEnumerable<Annotation>> CreateAnnotationAsync(string organization, Annotation annotation)
    {
        var key = annotation.GetFullKey(organization);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        if (await repository.Container.ExistsAsync(key))
        {
            throw new InvalidOperationException($"Annotation with key {key.Annotation} already exist.");
        }

        var newAnnotations = new List<Annotation>();

        switch (annotation.AnnotationType)
        {
            case AnnotationType.Responsibility:
            case AnnotationType.Subject:
            {
                await UpsertAnnotationEntityAsync(annotation, repository);
                break;
            }

            case AnnotationType.Unit:
            {
                var responsibilityKey = key.Annotation.GetResponsibilityKey();
                var unitName = key.Annotation.GetUnitName();

                var responsibilityTransaction = repository.Container.CreateTransactionalBatch(key.GetCosmosPartitionKey());

                await TryCheckAndCreateResponsibilityAsync(key, annotation, repository, responsibilityTransaction, newAnnotations);

                responsibilityTransaction.PatchItem(
                    key.GetCosmosItemId(responsibilityKey),
                    [PatchOperation.Add($"/{nameof(ResponsibilityEntity.Units)}/-", unitName)]);

                await UpsertAnnotationEntityAsync(annotation, responsibilityTransaction);
                await responsibilityTransaction.ExecuteAsync();
                break;
            }

            case AnnotationType.Usage:
            {
                var subjectKey = key.Annotation.GetSubjectKey();
                var subjectPartitionKey = new PartitionKey(key.GetPartitionKey(subjectKey));

                var responsibilityTransaction = repository.Container.CreateTransactionalBatch(key.GetCosmosPartitionKey());
                var subjectTransaction = repository.Container.CreateTransactionalBatch(subjectPartitionKey);

                await TryCheckAndCreateResponsibilityAsync(key, annotation, repository, responsibilityTransaction, newAnnotations);
                await TryCheckAndCreateSubjectAsync(key, annotation, repository, subjectTransaction, newAnnotations);

                subjectTransaction.PatchItem(
                    key.GetCosmosItemId(subjectKey),
                    [PatchOperation.Add($"/{nameof(SubjectEntity.Usages)}/-", key.Annotation.GetResponsibilityName())],
                    new TransactionalBatchPatchItemRequestOptions { EnableContentResponseOnWrite = false });

                await repository.Container.PatchItemAsync<SubjectEntity>(
                    key.GetCosmosItemId(subjectKey),
                    subjectPartitionKey,
                    [PatchOperation.Add($"/{nameof(SubjectEntity.Usages)}/-", key.Annotation.GetResponsibilityName())],
                    new PatchItemRequestOptions { EnableContentResponseOnWrite = false });

                await UpsertAnnotationEntityAsync(annotation, responsibilityTransaction);
                await Task.WhenAll(responsibilityTransaction.ExecuteAsync(), subjectTransaction.ExecuteAsync());
                break;
            }

            case AnnotationType.Context:
            {
                var subjectKey = key.Annotation.GetSubjectKey();
                var subjectPartitionKey = new PartitionKey(key.GetPartitionKey(subjectKey));

                var subjectTransaction = repository.Container.CreateTransactionalBatch(subjectPartitionKey);

                await TryCheckAndCreateSubjectAsync(key, annotation, repository, subjectTransaction, newAnnotations);

                subjectTransaction.PatchItem(
                    key.GetCosmosItemId(subjectKey),
                    [PatchOperation.Add($"/{nameof(SubjectEntity.Contexts)}/-", key.Annotation.GetContextName())]);

                await UpsertAnnotationEntityAsync(annotation, subjectTransaction);
                await subjectTransaction.ExecuteAsync();
                break;
            }

            case AnnotationType.Execution:
            {
                var contextKey = key.Annotation.GetContextKey();
                var subjectPartitionKey = new PartitionKey(key.GetPartitionKey(contextKey));
                var responsibilityName = key.Annotation.GetResponsibilityName();

                var responsibilityTransaction = repository.Container.CreateTransactionalBatch(key.GetCosmosPartitionKey());
                var subjectTransaction = repository.Container.CreateTransactionalBatch(subjectPartitionKey);

                await TryCheckAndCreateResponsibilityAsync(key, annotation, repository, responsibilityTransaction, newAnnotations);
                await TryCheckAndCreateSubjectAsync(key, annotation, repository, subjectTransaction, newAnnotations);
                await TryCheckAndCreateUsageAsync(key, annotation, repository, responsibilityTransaction, newAnnotations);
                await TryCheckAndCreateContextAsync(key, annotation, repository, subjectTransaction, newAnnotations);

                subjectTransaction.PatchItem(
                    key.GetCosmosItemId(contextKey),
                    [PatchOperation.Add($"/{nameof(ContextEntity.Executions)}/-", responsibilityName)],
                    new TransactionalBatchPatchItemRequestOptions { EnableContentResponseOnWrite = false });

                var usageKey = key.Annotation.GetUsageKey();
                var contextName = key.Annotation.GetContextName();

                responsibilityTransaction.PatchItem(
                    key.GetCosmosItemId(usageKey),
                    [PatchOperation.Add($"/{nameof(UsageEntity.Executions)}/-", contextName)],
                    new TransactionalBatchPatchItemRequestOptions { EnableContentResponseOnWrite = false });

                await UpsertAnnotationEntityAsync(annotation, responsibilityTransaction);
                await Task.WhenAll(responsibilityTransaction.ExecuteAsync(), subjectTransaction.ExecuteAsync());
                break;
            }

            case AnnotationType.UnitOfExecution:
            {
                var executionKey = key.Annotation.GetExecutionKey();
                var unitName = key.Annotation.GetUnitName();
                var subjectPartitionKey = new PartitionKey(key.GetPartitionKey(key.Annotation.GetSubjectKey()));

                var responsibilityTransaction = repository.Container.CreateTransactionalBatch(key.GetCosmosPartitionKey());
                var subjectTransaction = repository.Container.CreateTransactionalBatch(subjectPartitionKey);

                await TryCheckAndCreateResponsibilityAsync(key, annotation, repository, responsibilityTransaction, newAnnotations);
                await TryCheckAndCreateUnitAsync(key, annotation, repository, responsibilityTransaction, newAnnotations);
                await TryCheckAndCreateSubjectAsync(key, annotation, repository, subjectTransaction, newAnnotations);
                await TryCheckAndCreateUsageAsync(key, annotation, repository, responsibilityTransaction, newAnnotations);
                await TryCheckAndCreateContextAsync(key, annotation, repository, subjectTransaction, newAnnotations);
                await TryCheckAndCreateExecutionAsync(key, annotation, repository, responsibilityTransaction, newAnnotations);

                responsibilityTransaction.PatchItem(
                    key.GetCosmosItemId(executionKey),
                    [PatchOperation.Add($"/{nameof(ExecutionEntity.Units)}/-", unitName)]);

                await UpsertAnnotationEntityAsync(annotation, responsibilityTransaction);
                await Task.WhenAll(responsibilityTransaction.ExecuteAsync(), subjectTransaction.ExecuteAsync());
                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(annotation.AnnotationType));
        }

        newAnnotations.Add(annotation);
        return newAnnotations;
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

    public async Task<IEnumerable<Annotation>> CreateAnnotationsAsync(string organization, IEnumerable<Annotation> annotations)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        var annotationsInOrder = annotations
            .OrderBy(a => a.AnnotationType)
            .ToList();

        var createdAnnotations = new List<Annotation>();

        for (var index = 0; index < annotationsInOrder.Count; index++)
        {
            var annotation = annotationsInOrder[index];
            var key = annotation.GetFullKey(organization);

            if (await repository.Container.ExistsAsync(key))
            {
                annotationsInOrder.RemoveAt(index);
                index--;
                continue;
            }

            createdAnnotations.AddRange(await CreateAnnotationAsync(organization, annotation));
        }

        return createdAnnotations;
    }

    public Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, IEnumerable<string> annotations)
    {
        throw new NotImplementedException();
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
                            $"{usageKey.ViewName}.{AnnotationTypeCodes.Execution}.{subject.Name}.",
                            $"{usageKey.ViewName}.{AnnotationTypeCodes.UnitOfExecution}.{subject.Name}.",
                            usageKey.GetCosmosPartitionKey()));
                }

                // delete contexts and the subject
                // TODO: [P3] this could use delete all items by partition key
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
                // TODO: [P1] testing the DeleteAllItemsByPartitionKeyStreamAsync
                var deleteResponse = await repository.Container.DeleteAllItemsByPartitionKeyStreamAsync(fullKey.GetCosmosPartitionKey());
                deleteResponse.EnsureSuccessStatusCode();

                /*var queryable = repository.Container.GetItemLinqQueryable<Entity>(
                    requestOptions: new QueryRequestOptions() { PartitionKey = fullKey.GetCosmosPartitionKey(), },
                    allowSynchronousQueryExecution: true); // TODO: [P3] this should be replaced with non-blocking call

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
                await transaction.ExecuteAsync();*/

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

                var queryable = repository.Container.GetItemLinqQueryable<Entity>(
                    requestOptions: new QueryRequestOptions() { PartitionKey = fullKey.GetCosmosPartitionKey(), },
                    allowSynchronousQueryExecution: true); // TODO: [P3] this should be replaced with non-blocking call

                // delete all executions, unit-of-executions
                // TODO: [P3] this could use the storage procedure to delete a usage in responsibility partition
                var ids = queryable
                    .Where(entity => entity.Id.StartsWith($"{fullKey.ViewName}.{AnnotationTypeCodes.Execution}.{usage.SubjectName}.")
                                     || entity.Id.StartsWith($"{fullKey.ViewName}.{AnnotationTypeCodes.UnitOfExecution}.{usage.SubjectName}."))
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
            {
                var unit = (Unit)annotation;
                var responsibilityKey = FullKey.Create(fullKey.Annotation.GetResponsibilityKey(), fullKey);

                // remove unit from responsibility
                await repository.Container.RemoveArrayValueAsync(responsibilityKey.GetCosmosItemId(), nameof(ResponsibilityEntity.Units), unit.Name, responsibilityKey.GetCosmosPartitionKey());

                var queryable = repository.Container.GetItemLinqQueryable<Entity>(
                    requestOptions: new QueryRequestOptions() { PartitionKey = fullKey.GetCosmosPartitionKey(), },
                    allowSynchronousQueryExecution: true); // TODO: [P3] this should be replaced with non-blocking call

                // get all unit-of-executions of deleting unit
                var ids = queryable
                    .Where(entity => entity.Id.StartsWith($"{fullKey.ViewName}.{AnnotationTypeCodes.UnitOfExecution}.")
                                     && entity.Id.EndsWith($".{unit.Name}"))
                    .Select(entity => entity.Id)
                    .ToList();

                var transaction = repository.Container.CreateTransactionalBatch(fullKey.GetCosmosPartitionKey());

                foreach (var id in ids)
                {
                    transaction.DeleteItem(id);
                }

                // delete the unit
                transaction.DeleteItem(fullKey.GetCosmosItemId());
                await transaction.ExecuteAsync();

                break;
            }

            case AnnotationType.UnitOfExecution:
            {
                var unitOfExecution = (UnitOfExecution)annotation;
                var executionKey = FullKey.Create(fullKey.Annotation.GetExecutionKey(), fullKey);

                // remove unit from execution
                await repository.Container.RemoveArrayValueAsync(executionKey.GetCosmosItemId(), nameof(ExecutionEntity.Units), unitOfExecution.Name, executionKey.GetCosmosPartitionKey());

                // delete the unit of execution
                await DeleteAnnotationEntityAsync(fullKey, repository);

                break;
            }

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

    private async Task TryCheckAndCreateResponsibilityAsync(FullKey key, Annotation annotation, IContainerRepository repository, TransactionalBatch transaction, List<Annotation> newAnnotations)
    {
        var newKey = key.Annotation.GetResponsibilityKey();
        var partitionKey = new PartitionKey(key.GetPartitionKey(newKey));

        if (!await repository.Container.ExistsAsync(newKey, partitionKey))
        {
            var newAnnotation = new Responsibility()
            {
                AnnotationType = AnnotationType.Responsibility,
                AnnotationKey = newKey,
                Name = newKey.ResponsibilityName,
                ProjectName = annotation.ProjectName,
                ViewName = annotation.ViewName,
                ValidFrom = annotation.ValidFrom,
                ExpiresAt = annotation.ExpiresAt,
                TimeZone = annotation.TimeZone,
                IsDisabled = annotation.IsDisabled,
                Labels = annotation.Labels,
            };

            await UpsertAnnotationEntityAsync(newAnnotation, transaction);
            newAnnotations.Add(newAnnotation);
        }
    }

    private async Task TryCheckAndCreateUnitAsync(FullKey key, Annotation annotation, IContainerRepository repository, TransactionalBatch transaction, List<Annotation> newAnnotations)
    {
        var newKey = key.Annotation.GetUnitKey();
        var partitionKey = new PartitionKey(key.GetPartitionKey(newKey));

        if (!await repository.Container.ExistsAsync(newKey, partitionKey))
        {
            var newAnnotation = new Unit()
            {
                AnnotationType = AnnotationType.Unit,
                AnnotationKey = newKey,
                Name = newKey.UnitName,
                ResponsibilityKey = newKey.GetResponsibilityKey(),
                ResponsibilityName = newKey.GetResponsibilityName(),
                ProjectName = annotation.ProjectName,
                ViewName = annotation.ViewName,
                ValidFrom = annotation.ValidFrom,
                ExpiresAt = annotation.ExpiresAt,
                TimeZone = annotation.TimeZone,
                IsDisabled = annotation.IsDisabled,
                Labels = annotation.Labels,
            };

            await UpsertAnnotationEntityAsync(newAnnotation, transaction);
            newAnnotations.Add(newAnnotation);
        }
    }

    private async Task TryCheckAndCreateSubjectAsync(FullKey key, Annotation annotation, IContainerRepository repository, TransactionalBatch transaction, List<Annotation> newAnnotations)
    {
            var newKey = key.Annotation.GetSubjectKey();
            var partitionKey = new PartitionKey(key.GetPartitionKey(newKey));

            if (!await repository.Container.ExistsAsync(newKey, partitionKey))
            {
                var newAnnotation = new Subject()
                {
                    AnnotationType = AnnotationType.Subject,
                    AnnotationKey = newKey,
                    Name = newKey.SubjectName,
                    ProjectName = annotation.ProjectName,
                    ViewName = annotation.ViewName,
                    ValidFrom = annotation.ValidFrom,
                    ExpiresAt = annotation.ExpiresAt,
                    TimeZone = annotation.TimeZone,
                    IsDisabled = annotation.IsDisabled,
                    Labels = annotation.Labels,
                };

                await UpsertAnnotationEntityAsync(newAnnotation, transaction);
                newAnnotations.Add(newAnnotation);
            }
    }

    private async Task TryCheckAndCreateUsageAsync(FullKey key, Annotation annotation, IContainerRepository repository, TransactionalBatch transaction, List<Annotation> newAnnotations)
    {
        var newKey = key.Annotation.GetUsageKey();
        var partitionKey = new PartitionKey(key.GetPartitionKey(newKey));

        if (!await repository.Container.ExistsAsync(newKey, partitionKey))
        {
            var newAnnotation = new Usage()
            {
                AnnotationType = AnnotationType.Usage,
                AnnotationKey = newKey,
                Name = newKey.ResponsibilityName,
                ResponsibilityKey = newKey.GetResponsibilityKey(),
                ResponsibilityName = newKey.GetResponsibilityName(),
                SubjectKey = newKey.GetSubjectKey(),
                SubjectName = newKey.GetSubjectName(),
                ProjectName = annotation.ProjectName,
                ViewName = annotation.ViewName,
                ValidFrom = annotation.ValidFrom,
                ExpiresAt = annotation.ExpiresAt,
                TimeZone = annotation.TimeZone,
                IsDisabled = annotation.IsDisabled,
                Labels = annotation.Labels,
            };

            await UpsertAnnotationEntityAsync(newAnnotation, transaction);
            newAnnotations.Add(newAnnotation);
        }
    }

    private async Task TryCheckAndCreateContextAsync(FullKey key, Annotation annotation, IContainerRepository repository, TransactionalBatch transaction, List<Annotation> newAnnotations)
    {
        var newKey = key.Annotation.GetContextKey();
        var partitionKey = new PartitionKey(key.GetPartitionKey(newKey));

        if (!await repository.Container.ExistsAsync(newKey, partitionKey))
        {
            var newAnnotation = new Context()
            {
                AnnotationType = AnnotationType.Context,
                AnnotationKey = newKey,
                Name = newKey.ContextName,
                SubjectKey = newKey.GetSubjectKey(),
                SubjectName = newKey.GetSubjectName(),
                ProjectName = annotation.ProjectName,
                ViewName = annotation.ViewName,
                ValidFrom = annotation.ValidFrom,
                ExpiresAt = annotation.ExpiresAt,
                TimeZone = annotation.TimeZone,
                IsDisabled = annotation.IsDisabled,
                Labels = annotation.Labels,
            };

            await UpsertAnnotationEntityAsync(newAnnotation, transaction);
            newAnnotations.Add(newAnnotation);
        }
    }

    private async Task TryCheckAndCreateExecutionAsync(FullKey key, Annotation annotation, IContainerRepository repository, TransactionalBatch transaction, List<Annotation> newAnnotations)
    {
        var newKey = key.Annotation.GetExecutionKey();
        var partitionKey = new PartitionKey(key.GetPartitionKey(newKey));

        if (!await repository.Container.ExistsAsync(newKey, partitionKey))
        {
            var newAnnotation = new Execution()
            {
                AnnotationType = AnnotationType.Execution,
                AnnotationKey = newKey,
                Name = newKey.ContextName,
                ResponsibilityKey = newKey.GetResponsibilityKey(),
                ResponsibilityName = newKey.GetResponsibilityName(),
                SubjectKey = newKey.GetSubjectKey(),
                SubjectName = newKey.GetSubjectName(),
                ContextKey = newKey.GetContextKey(),
                ContextName = newKey.GetContextName(),
                ProjectName = annotation.ProjectName,
                ViewName = annotation.ViewName,
                ValidFrom = annotation.ValidFrom,
                ExpiresAt = annotation.ExpiresAt,
                TimeZone = annotation.TimeZone,
                IsDisabled = annotation.IsDisabled,
                Labels = annotation.Labels,
            };

            await UpsertAnnotationEntityAsync(newAnnotation, transaction);
            newAnnotations.Add(newAnnotation);
        }
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
