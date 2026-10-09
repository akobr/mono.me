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

    public async Task<IReadOnlyList<ConfigurationTemplateSummary>> ListTemplatesAsync(string organization, string project, string view)
    {
        // Current templates only: "{view}.gen." excludes the state ("gns") and history ("gnv") items.
        var prefix = $"{view}.{EntityIdPrefixTypes.GenerateTemplate}.";
        var query = new QueryDefinition("SELECT c.id, c.Version, c.Author, c._ts FROM c WHERE STARTSWITH(c.id, @prefix)")
            .WithParameter("@prefix", prefix);

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        using var iterator = repository.Container.GetItemQueryIterator<DocumentListingRow>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = PartitionKeys.GetCosmosTemplate(project) });

        var summaries = new List<ConfigurationTemplateSummary>();

        while (iterator.HasMoreResults)
        {
            summaries.AddRange((await iterator.ReadNextAsync()).Select(row => new ConfigurationTemplateSummary
            {
                AnnotationType = row.Id[prefix.Length..],
                Version = row.Version,
                Author = row.Author,
                UpdatedAt = ListingTimestamps.ToUpdatedAt(row.Timestamp),
            }));
        }

        return summaries.OrderBy(summary => summary.AnnotationType, StringComparer.Ordinal).ToList();
    }

    public async Task<ConfigurationTemplate?> GetTemplateAsync(string organization, string project, string view, string annotationType)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var entity = await repository.Container.TryReadItemAsync(
            GetTemplateId(view, annotationType),
            PartitionKeys.GetCosmosTemplate(project),
            stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));

        return entity?.ToConfigurationTemplate();
    }

    public async Task<ConfigurationTemplate> CreateOrUpdateTemplateAsync(
        string organization,
        string project,
        string view,
        string annotationType,
        JObject value,
        string author)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = PartitionKeys.GetTemplate(project);
        var partitionKey = new PartitionKey(partitionKeyValue);
        var id = GetTemplateId(view, annotationType);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await ReadTemplateAsync(repository, partitionKey, id);
            var (state, stateEtag) = await ReadStateAsync(repository, partitionKey, view, annotationType);

            // the input is mutated by the requested operations, each attempt works on a fresh copy
            var input = (JObject)value.DeepClone();

            if (existing is null)
            {
                // create a new template if there is nothing yet
                input = input.RemoveRequested();
                input = await input.ApplyPatchRequested();

                // the state keeps the last version even after a deletion and an expiration of the history
                var lastVersion = state?.LastVersion ?? await GetMaxHistoryVersionAsync(repository, partitionKey, view, annotationType);
                var template = new GenerateTemplateEntity
                {
                    PartitionKey = partitionKeyValue,
                    Id = id,
                    AnnotationKey = annotationType,
                    Name = annotationType,
                    ProjectName = project,
                    ViewName = view,
                    Content = input,
                    Author = author,
                    Version = lastVersion + 1,
                };

                // the version allocation and the creation are atomic, guarded by the state etag
                var createBatch = repository.Container.CreateTransactionalBatch(partitionKey);
                createBatch.CreateItem(template);
                AddStateWrite(createBatch, template, state, stateEtag, template.Version);
                var createdStateEtag = await TryExecuteWriteAsync(createBatch, id);
                if (createdStateEtag is null)
                {
                    continue;
                }

                await InvalidateAsync(repository, partitionKey, project, view, annotationType, createdStateEtag);
                return template.ToConfigurationTemplate();
            }

            var newContent = (JObject)existing.Content.DeepClone();
            newContent.MergeInto(input);
            newContent = newContent.RemoveRequested();
            newContent = await newContent.ApplyPatchRequested();

            if (JToken.DeepEquals(existing.Content, newContent))
            {
                // no change after merge, but finish an invalidation of an earlier committed change
                await CompletePendingInvalidationAsync(repository, partitionKey, project, view, annotationType, state, stateEtag);
                return existing.ToConfigurationTemplate();
            }

            var updated = await TryWriteNextVersionAsync(repository, partitionKey, project, existing, etag!, state, stateEtag, newContent, author);
            if (updated is null)
            {
                continue;
            }

            return updated.ToConfigurationTemplate();
        }

        throw new TemplateConcurrencyException(project, view, annotationType, MaxRetries);
    }

    public async Task<ConfigurationTemplate> PatchTemplateAsync(
        string organization,
        string project,
        string view,
        string annotationType,
        JArray patchOperations,
        string author)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = PartitionKeys.GetCosmosTemplate(project);
        var id = GetTemplateId(view, annotationType);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await ReadTemplateAsync(repository, partitionKey, id);
            var (state, stateEtag) = await ReadStateAsync(repository, partitionKey, view, annotationType);

            if (existing is null)
            {
                await CompletePendingInvalidationAsync(repository, partitionKey, project, view, annotationType, state, stateEtag);
                throw new TemplateNotFoundException(project, view, annotationType);
            }

            var newContent = await existing.Content.ApplyPatch(patchOperations);

            if (JToken.DeepEquals(existing.Content, newContent))
            {
                await CompletePendingInvalidationAsync(repository, partitionKey, project, view, annotationType, state, stateEtag);
                return existing.ToConfigurationTemplate();
            }

            var updated = await TryWriteNextVersionAsync(repository, partitionKey, project, existing, etag!, state, stateEtag, newContent, author);
            if (updated is null)
            {
                continue;
            }

            return updated.ToConfigurationTemplate();
        }

        throw new TemplateConcurrencyException(project, view, annotationType, MaxRetries);
    }

    public async Task<bool> DeleteTemplateAsync(string organization, string project, string view, string annotationType)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = PartitionKeys.GetCosmosTemplate(project);
        var id = GetTemplateId(view, annotationType);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await ReadTemplateAsync(repository, partitionKey, id);
            var (state, stateEtag) = await ReadStateAsync(repository, partitionKey, view, annotationType);

            if (existing is null)
            {
                // already deleted, but finish an invalidation of an earlier committed deletion
                await CompletePendingInvalidationAsync(repository, partitionKey, project, view, annotationType, state, stateEtag);
                return false;
            }

            // save history of the deleted version together with the deletion atomically
            var batch = repository.Container.CreateTransactionalBatch(partitionKey);
            batch.CreateItem(existing.ToHistory());
            batch.DeleteItem(id, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
            AddStateWrite(batch, existing, state, stateEtag, existing.Version);
            var newStateEtag = await TryExecuteWriteAsync(batch, id);
            if (newStateEtag is null)
            {
                continue;
            }

            await InvalidateAsync(repository, partitionKey, project, view, annotationType, newStateEtag);
            return true;
        }

        throw new TemplateConcurrencyException(project, view, annotationType, MaxRetries);
    }

    public async Task<IReadOnlyCollection<ConfigurationVersion>> GetTemplateVersionsAsync(string organization, string project, string view, string annotationType)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = PartitionKeys.GetCosmosTemplate(project);

        // TODO: [P3] optimize the query to ignore Content
        var feed = repository.Container.GetItemLinqQueryable<GenerateTemplateHistoryEntity>(
                requestOptions: new QueryRequestOptions { PartitionKey = partitionKey })
            .Where(history => history.Id.StartsWith(GetTemplateVersionIdPrefix(view, annotationType)))
            .OrderBy(history => history.Version)
            .ToFeedIterator();

        var versions = new List<ConfigurationVersion>();
        while (feed.HasMoreResults)
        {
            var results = await feed.ReadNextAsync();
            versions.AddRange(results.Select(entity => entity.ToConfigurationVersion()));
        }

        var template = await repository.Container.TryReadItemAsync(
            GetTemplateId(view, annotationType),
            partitionKey,
            stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));

        if (template is not null)
        {
            versions.Add(template.ToConfigurationVersion());
        }

        return versions;
    }

    public async Task<ConfigurationTemplate?> GetTemplateVersionContentAsync(string organization, string project, string view, string annotationType, uint version)
    {
        annotationType = NormalizeAnnotationType(annotationType);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = PartitionKeys.GetCosmosTemplate(project);
        var versionEntity = await repository.Container.TryReadItemAsync(
            $"{GetTemplateVersionIdPrefix(view, annotationType)}{version}",
            partitionKey,
            stream => stream.DeserializeNewtonsoft<GenerateTemplateHistoryEntity>(_serializerOptions));

        if (versionEntity is not null)
        {
            return versionEntity.ToConfigurationTemplate();
        }

        var template = await repository.Container.TryReadItemAsync(
            GetTemplateId(view, annotationType),
            partitionKey,
            stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));

        if (template is null
            || template.Version != version)
        {
            return null;
        }

        return template.ToConfigurationTemplate();
    }

    public Task<DiffResult> GetTemplateVersionChangesAsync(string organization, string project, string view, string annotationType, uint version)
    {
        return GetTemplateVersionChangesAsync(organization, project, view, annotationType, version == 0 ? 0 : version - 1, version);
    }

    public Task<DiffResult> GetTemplateVersionChangesAsync(string organization, string project, string view, string annotationType, uint fromVersion, uint toVersion)
    {
        return _differ.GetChangesAsync(
            async () => fromVersion == 0 ? new JObject() : (await GetTemplateVersionContentAsync(organization, project, view, annotationType, fromVersion))?.Content,
            async () => toVersion == 0 ? new JObject() : (await GetTemplateVersionContentAsync(organization, project, view, annotationType, toVersion))?.Content,
            $"Unknown version {fromVersion} of the template for {annotationType} in view {view}.",
            $"Unknown version {toVersion} of the template for {annotationType} in view {view}.");
    }

    internal static string GetTemplateId(string view, string annotationType)
    {
        return $"{view}.{EntityIdPrefixTypes.GenerateTemplate}.{annotationType}";
    }

    internal static string GetTemplateStateId(string view, string annotationType)
    {
        return $"{view}.{EntityIdPrefixTypes.GenerateTemplateState}.{annotationType}";
    }

    private static string GetTemplateVersionIdPrefix(string view, string annotationType)
    {
        return $"{view}.{EntityIdPrefixTypes.GenerateTemplateVersion}.{annotationType}.";
    }

    private Task<(GenerateTemplateEntity? Item, string? ETag)> ReadTemplateAsync(IContainerRepository repository, PartitionKey partitionKey, string id)
    {
        return repository.Container.TryReadItemWithETagAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));
    }

    private Task<(GenerateTemplateStateEntity? Item, string? ETag)> ReadStateAsync(IContainerRepository repository, PartitionKey partitionKey, string view, string annotationType)
    {
        return repository.Container.TryReadItemWithETagAsync(
            GetTemplateStateId(view, annotationType),
            partitionKey,
            stream => stream.DeserializeNewtonsoft<GenerateTemplateStateEntity>(_serializerOptions));
    }

    private static async Task<ulong> GetMaxHistoryVersionAsync(IContainerRepository repository, PartitionKey partitionKey, string view, string annotationType)
    {
        // fallback for templates written before the state record existed
        var maxVersionResponse = await repository.Container.GetItemLinqQueryable<GenerateTemplateHistoryEntity>(
                requestOptions: new QueryRequestOptions { PartitionKey = partitionKey })
            .Where(history => history.Id.StartsWith(GetTemplateVersionIdPrefix(view, annotationType)))
            .Select(history => history.Version)
            .MaxAsync();

        return maxVersionResponse.Resource;
    }

    private static async Task<GenerateTemplateEntity?> TryWriteNextVersionAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        string project,
        GenerateTemplateEntity existing,
        string etag,
        GenerateTemplateStateEntity? state,
        string? stateEtag,
        JObject newContent,
        string author)
    {
        var updated = existing with
        {
            Version = Math.Max(existing.Version, state?.LastVersion ?? 0) + 1,
            Content = newContent,
            Author = author,
        };

        // save history of the previous version together with the next version atomically
        var batch = repository.Container.CreateTransactionalBatch(partitionKey);
        batch.CreateItem(existing.ToHistory());
        batch.ReplaceItem(existing.Id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
        AddStateWrite(batch, existing, state, stateEtag, updated.Version);
        var newStateEtag = await TryExecuteWriteAsync(batch, existing.Id);
        if (newStateEtag is null)
        {
            return null;
        }

        await InvalidateAsync(repository, partitionKey, project, existing.ViewName, existing.Name, newStateEtag);
        return updated;
    }

    private static void AddStateWrite(
        TransactionalBatch batch,
        GenerateTemplateEntity template,
        GenerateTemplateStateEntity? state,
        string? stateEtag,
        ulong version)
    {
        // the state write is always the last operation of the batch, its etag is read from the response
        var newState = new GenerateTemplateStateEntity
        {
            PartitionKey = template.PartitionKey,
            Id = GetTemplateStateId(template.ViewName, template.Name),
            AnnotationKey = template.AnnotationKey,
            Name = template.Name,
            ProjectName = template.ProjectName,
            ViewName = template.ViewName,
            LastVersion = Math.Max(state?.LastVersion ?? 0, version),
            IsInvalidationPending = true,
        };

        if (state is null)
        {
            batch.CreateItem(newState);
        }
        else
        {
            batch.ReplaceItem(newState.Id, newState, new TransactionalBatchItemRequestOptions { IfMatchEtag = stateEtag });
        }
    }

    /// <summary>
    /// Executes a template write batch; returns the new etag of the state record, or null on a concurrent modification.
    /// </summary>
    private static async Task<string?> TryExecuteWriteAsync(TransactionalBatch batch, string id)
    {
        using var response = await batch.ExecuteAsync();

        // history of the version already archived or state created (409), template or state changed (412), or template deleted (404) by another writer
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ConfigurationStorageException(
                $"Failed to write template '{id}' with status {response.StatusCode}: {response.ErrorMessage}",
                response.StatusCode);
        }

        return response[response.Count - 1].ETag;
    }

    private static Task CompletePendingInvalidationAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        string project,
        string view,
        string annotationType,
        GenerateTemplateStateEntity? state,
        string? stateEtag)
    {
        return state?.IsInvalidationPending == true
            ? InvalidateAsync(repository, partitionKey, project, view, annotationType, stateEtag!)
            : Task.CompletedTask;
    }

    private static async Task InvalidateAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        string project,
        string view,
        string annotationType,
        string stateEtag)
    {
        // a failure leaves the pending flag set, a repeated request completes the invalidation
        await ConfigurationCacheInvalidator.InvalidateForTemplateAsync(repository, project, view, annotationType);

        // clear the flag only if nobody changed the template meanwhile, otherwise their invalidation is still pending
        using var response = await repository.Container.PatchItemStreamAsync(
            GetTemplateStateId(view, annotationType),
            partitionKey,
            [PatchOperation.Set($"/{nameof(GenerateTemplateStateEntity.IsInvalidationPending)}", false)],
            new PatchItemRequestOptions { IfMatchEtag = stateEtag });
    }

    private static string NormalizeAnnotationType(string annotationType)
    {
        var normalized = annotationType.ToLowerInvariant();

        return AnnotationTypeCodes.ValidCodes.ContainsKey(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(annotationType), annotationType, "Unknown annotation type code.");
    }
}
