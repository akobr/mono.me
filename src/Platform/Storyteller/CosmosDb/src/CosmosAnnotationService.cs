using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace _42.Platform.Storyteller;

public class CosmosAnnotationService : IAnnotationService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly JsonNodeOptions JsonNodeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IContainerRepositoryProvider _repositoryProvider;

    public CosmosAnnotationService(IContainerRepositoryProvider repositoryProvider)
    {
        _repositoryProvider = repositoryProvider;
    }

    public async Task<Annotation?> GetAnnotationAsync(FullKey fullKey)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(fullKey.OrganizationName);
        using var response = await repository.Container.ReadItemStreamAsync(fullKey.GetCosmosItemKey(), fullKey.GetCosmosPartitionKey());

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

        var annotationType = (AnnotationType)typeProperty.GetValue<int>();
        var annotationTypeDefinition = AnnotationTypeCodes.GetRealType(annotationType);

        return (Annotation)jData.Deserialize(annotationTypeDefinition, JsonSerializerOptions)!;
    }

    public async Task<Annotation?> GetAnnotationAsync(string fullKey)
    {
        var key = FullKey.Parse(fullKey);
        return await GetAnnotationAsync(key);
    }

    public async Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request)
    {
        var model = new RequestProcessModel(request);

        var processFunctionsInOrder = new Func<RequestProcessModel, Task>[]
        {
            m => GetAnnotationsAsync<Responsibility>(m, AnnotationType.Responsibility, model.PartitionKey),
            m => GetAnnotationsAsync<Subject>(m, AnnotationType.Subject, model.PartitionKey),
            m => GetAnnotationsAsync<Usage>(m, AnnotationType.Usage, model.PartitionKey),
            m => GetAnnotationsAsync<Context>(m, AnnotationType.Context, model.PartitionKey),
            m => GetAnnotationsAsync<Execution>(m, AnnotationType.Execution, model.PartitionKey),
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

    public async Task CreateOrUpdateAnnotationAsync(string organization, Annotation annotation)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        using var memoryStream = new MemoryStream();
        await JsonSerializer.SerializeAsync(
            memoryStream,
            annotation,
            AnnotationTypeMap.GetSystemType(annotation.AnnotationType),
            new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            });

        memoryStream.Seek(0, SeekOrigin.Begin);
        await repository.Container.UpsertItemStreamAsync(memoryStream, new PartitionKey(annotation.PartitionKey));
    }

    private async Task GetAnnotationsAsync<TAnnotation>(RequestProcessModel model, AnnotationType annotationType, PartitionKey? partitionKey = null)
        where TAnnotation : Annotation
    {
        var annotationTypeName = Enum.GetName(annotationType) ?? "Unknown";

        if (!model.TypesMap.Contains(annotationType)
            || (model.InputContinuationToken is not null && model.InputContinuationToken.Type != annotationTypeName))
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
            .Where(b => b.Against == AnnotationType.Responsibility)
            .Cast<AnnotationsRequest.Condition<TAnnotation>>();

        var repository = _repositoryProvider.GetOrganizationContainer(model.Request.Organization);
        var queryable = repository.Container.GetItemLinqQueryable<TAnnotation>(
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
        model.Annotations = model.Annotations.Concat(results);
        model.TotalCount += results.Count;

        if (feed.HasMoreResults)
        {
            model.OutputContinuationToken = new ContinuationToken(annotationTypeName, results.ContinuationToken);
        }
    }
}
