using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;
using _42.Platform.Storyteller.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

public class CosmosConfigurationTemplateService : IConfigurationTemplateService
{
    private const int MaxRetries = 3;

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerSettings _serializerOptions;
    private readonly JsonContentDiffer _differ;

    public CosmosConfigurationTemplateService(
        IContainerRepositoryProvider repositoryProvider,
        IJsonSerializationSettingsProvider jsonSettingsProvider,
        IOptions<JsonSerializerSettings> serializerOptions)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _differ = new JsonContentDiffer(jsonSettingsProvider);
    }

    public async Task<ConfigurationTemplate?> GetTemplateAsync(string organization, string project, string annotationType)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var entity = await repository.Container.TryReadItemAsync(
            GetTemplateId(annotationType),
            PartitionKeys.GetCosmosTemplate(project),
            stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));

        return entity?.ToConfigurationTemplate();
    }

    public async Task<ConfigurationTemplate> CreateOrUpdateTemplateAsync(
        string organization,
        string project,
        string annotationType,
        JObject value,
        string author)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = PartitionKeys.GetTemplate(project);
        var partitionKey = new PartitionKey(partitionKeyValue);
        var id = GetTemplateId(annotationType);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await repository.Container.TryReadItemWithETagAsync(
                id,
                partitionKey,
                stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));

            // the input is mutated by the requested operations, each attempt works on a fresh copy
            var input = (JObject)value.DeepClone();

            if (existing is null)
            {
                // create a new template if there is nothing yet
                input = input.RemoveRequested();
                input = await input.ApplyPatchRequested();

                var maxVersionResponse = await repository.Container.GetItemLinqQueryable<GenerateTemplateHistoryEntity>(
                        requestOptions: new QueryRequestOptions { PartitionKey = partitionKey })
                    .Where(history => history.Id.StartsWith($"{EntityIdPrefixTypes.GenerateTemplateVersion}.{annotationType}."))
                    .Select(history => history.Version)
                    .MaxAsync();

                var template = new GenerateTemplateEntity
                {
                    PartitionKey = partitionKeyValue,
                    Id = id,
                    AnnotationKey = annotationType,
                    Name = annotationType,
                    ProjectName = project,
                    ViewName = string.Empty,
                    Content = input,
                    Author = author,
                    Version = maxVersionResponse.Resource + 1,
                };

                try
                {
                    await repository.Container.CreateItemAsync(template, partitionKey);
                }
                catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
                {
                    // created by another writer meanwhile; retry as an update
                    continue;
                }

                await ConfigurationCacheInvalidator.InvalidateForTemplateAsync(repository, project, annotationType);
                return template.ToConfigurationTemplate();
            }

            var newContent = (JObject)existing.Content.DeepClone();
            newContent.MergeInto(input);
            newContent = newContent.RemoveRequested();
            newContent = await newContent.ApplyPatchRequested();

            if (JToken.DeepEquals(existing.Content, newContent))
            {
                // no change after merge
                return existing.ToConfigurationTemplate();
            }

            var updated = await TryWriteNextVersionAsync(repository.Container, partitionKey, existing, etag!, newContent, author);
            if (updated is null)
            {
                continue;
            }

            await ConfigurationCacheInvalidator.InvalidateForTemplateAsync(repository, project, annotationType);
            return updated.ToConfigurationTemplate();
        }

        throw CreateConcurrencyException(project, annotationType);
    }

    public async Task<ConfigurationTemplate> PatchTemplateAsync(
        string organization,
        string project,
        string annotationType,
        JArray patchOperations,
        string author)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = PartitionKeys.GetCosmosTemplate(project);
        var id = GetTemplateId(annotationType);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await repository.Container.TryReadItemWithETagAsync(
                id,
                partitionKey,
                stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));

            if (existing is null)
            {
                throw new TemplateNotFoundException(project, annotationType);
            }

            var newContent = await existing.Content.ApplyPatch(patchOperations);

            if (JToken.DeepEquals(existing.Content, newContent))
            {
                return existing.ToConfigurationTemplate();
            }

            var updated = await TryWriteNextVersionAsync(repository.Container, partitionKey, existing, etag!, newContent, author);
            if (updated is null)
            {
                continue;
            }

            await ConfigurationCacheInvalidator.InvalidateForTemplateAsync(repository, project, annotationType);
            return updated.ToConfigurationTemplate();
        }

        throw CreateConcurrencyException(project, annotationType);
    }

    public async Task<bool> DeleteTemplateAsync(string organization, string project, string annotationType)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = PartitionKeys.GetCosmosTemplate(project);
        var id = GetTemplateId(annotationType);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await repository.Container.TryReadItemWithETagAsync(
                id,
                partitionKey,
                stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));

            if (existing is null)
            {
                return false;
            }

            // save history of the deleted version together with the deletion atomically
            var batch = repository.Container.CreateTransactionalBatch(partitionKey);
            batch.CreateItem(existing.ToHistory());
            batch.DeleteItem(id, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
            using var response = await batch.ExecuteAsync();

            if (IsConcurrentModification(response))
            {
                continue;
            }

            EnsureSuccess(response, id);
            await ConfigurationCacheInvalidator.InvalidateForTemplateAsync(repository, project, annotationType);
            return true;
        }

        throw CreateConcurrencyException(project, annotationType);
    }

    public async Task<IReadOnlyCollection<ConfigurationVersion>> GetTemplateVersionsAsync(string organization, string project, string annotationType)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = PartitionKeys.GetCosmosTemplate(project);

        // TODO: [P3] optimize the query to ignore Content
        var feed = repository.Container.GetItemLinqQueryable<GenerateTemplateHistoryEntity>(
                requestOptions: new QueryRequestOptions { PartitionKey = partitionKey })
            .Where(history => history.Id.StartsWith($"{EntityIdPrefixTypes.GenerateTemplateVersion}.{annotationType}."))
            .OrderBy(history => history.Version)
            .ToFeedIterator();

        var versions = new List<ConfigurationVersion>();
        while (feed.HasMoreResults)
        {
            var results = await feed.ReadNextAsync();
            versions.AddRange(results.Select(entity => entity.ToConfigurationVersion()));
        }

        var template = await repository.Container.TryReadItemAsync(
            GetTemplateId(annotationType),
            partitionKey,
            stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));

        if (template is not null)
        {
            versions.Add(template.ToConfigurationVersion());
        }

        return versions;
    }

    public async Task<ConfigurationTemplate?> GetTemplateVersionContentAsync(string organization, string project, string annotationType, uint version)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = PartitionKeys.GetCosmosTemplate(project);
        var versionEntity = await repository.Container.TryReadItemAsync(
            $"{EntityIdPrefixTypes.GenerateTemplateVersion}.{annotationType}.{version}",
            partitionKey,
            stream => stream.DeserializeNewtonsoft<GenerateTemplateHistoryEntity>(_serializerOptions));

        if (versionEntity is not null)
        {
            return versionEntity.ToConfigurationTemplate();
        }

        var template = await repository.Container.TryReadItemAsync(
            GetTemplateId(annotationType),
            partitionKey,
            stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));

        if (template is null
            || template.Version != version)
        {
            return null;
        }

        return template.ToConfigurationTemplate();
    }

    public Task<DiffResult> GetTemplateVersionChangesAsync(string organization, string project, string annotationType, uint version)
    {
        return GetTemplateVersionChangesAsync(organization, project, annotationType, version - 1, version);
    }

    public Task<DiffResult> GetTemplateVersionChangesAsync(string organization, string project, string annotationType, uint fromVersion, uint toVersion)
    {
        return _differ.GetChangesAsync(
            async () => fromVersion == 0 ? new JObject() : (await GetTemplateVersionContentAsync(organization, project, annotationType, fromVersion))?.Content,
            async () => toVersion == 0 ? new JObject() : (await GetTemplateVersionContentAsync(organization, project, annotationType, toVersion))?.Content,
            $"Unknown version {fromVersion} of the template for {annotationType}.",
            $"Unknown version {toVersion} of the template for {annotationType}.");
    }

    internal static string GetTemplateId(string annotationType)
    {
        return $"{EntityIdPrefixTypes.GenerateTemplate}.{annotationType}";
    }

    private static async Task<GenerateTemplateEntity?> TryWriteNextVersionAsync(
        Container container,
        PartitionKey partitionKey,
        GenerateTemplateEntity existing,
        string etag,
        JObject newContent,
        string author)
    {
        var updated = existing with
        {
            Version = existing.Version + 1,
            Content = newContent,
            Author = author,
        };

        // save history of the previous version together with the next version atomically
        var batch = container.CreateTransactionalBatch(partitionKey);
        batch.CreateItem(existing.ToHistory());
        batch.ReplaceItem(existing.Id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
        using var response = await batch.ExecuteAsync();

        if (IsConcurrentModification(response))
        {
            return null;
        }

        EnsureSuccess(response, existing.Id);
        return updated;
    }

    private static bool IsConcurrentModification(TransactionalBatchResponse response)
    {
        // history of the version already archived (409), template changed (412) or deleted (404) by another writer
        return response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound;
    }

    private static void EnsureSuccess(TransactionalBatchResponse response, string id)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to write template '{id}' with status {response.StatusCode}: {response.ErrorMessage}");
        }
    }

    private static InvalidOperationException CreateConcurrencyException(string project, string annotationType)
    {
        return new InvalidOperationException(
            $"Failed to update template '{annotationType}' in project '{project}' after multiple retries ({MaxRetries}) due to concurrent modifications.");
    }

    private static string NormalizeAnnotationType(string annotationType)
    {
        var normalized = annotationType.ToLowerInvariant();

        return AnnotationTypeCodes.ValidCodes.ContainsKey(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(annotationType), annotationType, "Unknown annotation type code.");
    }
}
