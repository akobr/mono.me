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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NJsonSchema;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

public class CosmosConfigurationSchemaService : IConfigurationSchemaService
{
    private const string SchemaPartitionSuffix = "schema";
    private const int MaxRetries = 3;

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerSettings _serializerOptions;
    private readonly JsonContentDiffer _differ;
    private readonly ILogger<CosmosConfigurationSchemaService> _logger;

    public CosmosConfigurationSchemaService(
        IContainerRepositoryProvider repositoryProvider,
        IJsonSerializationSettingsProvider jsonSettingsProvider,
        IOptions<JsonSerializerSettings> serializerOptions,
        ILogger<CosmosConfigurationSchemaService> logger)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _differ = new JsonContentDiffer(jsonSettingsProvider);
        _logger = logger;
    }

    private enum SchemaKind
    {
        Type,
        Annotation,
        Descendant,
    }

    public async Task<IReadOnlyList<ConfigurationSchemaSummary>> ListSchemasAsync(string organization, string project, string view)
    {
        // Current schema documents only: "{view}.cfs." excludes the state ("css") and history ("csv") items.
        var prefix = $"{view}.{EntityIdPrefixTypes.ConfigurationSchema}.";
        var query = new QueryDefinition("SELECT c.id, c.Version, c.Author, c._ts FROM c WHERE STARTSWITH(c.id, @prefix)")
            .WithParameter("@prefix", prefix);

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        using var iterator = repository.Container.GetItemQueryIterator<DocumentListingRow>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(GetSchemaPartitionKey(project)) });

        var summaries = new List<ConfigurationSchemaSummary>();

        while (iterator.HasMoreResults)
        {
            foreach (var row in await iterator.ReadNextAsync())
            {
                var summary = ToSchemaSummary(row, row.Id[prefix.Length..]);

                if (summary is not null)
                {
                    summaries.Add(summary);
                }
            }
        }

        return summaries
            .OrderBy(summary => summary.Kind)
            .ThenBy(summary => summary.AnnotationType, StringComparer.Ordinal)
            .ThenBy(summary => summary.AnnotationKey, StringComparer.Ordinal)
            .ToList();
    }

    public Task<ConfigurationSchema?> GetSchemaAsync(string organization, string project, string view, string annotationType)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return ReadCurrentAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema> SetSchemaAsync(
        string organization,
        string project,
        string view,
        string annotationType,
        JObject schemaContent,
        string author,
        bool force)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return SaveAsync(organization, project, view, identity, schemaContent, author, force);
    }

    public Task<bool> DeleteSchemaAsync(string organization, string project, string view, string annotationType)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return DeleteAsync(organization, project, view, identity);
    }

    public Task<ConfigurationSchema?> GetAnnotationSchemaAsync(string organization, string project, string view, string annotationKey)
    {
        var identity = SchemaIdentity.ForAnnotation(view, AnnotationKey.Parse(annotationKey).ToString());
        return ReadCurrentAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema> SetAnnotationSchemaAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        JObject schemaContent,
        string author,
        bool force)
    {
        var identity = SchemaIdentity.ForAnnotation(view, AnnotationKey.Parse(annotationKey).ToString());
        return SaveAsync(organization, project, view, identity, schemaContent, author, force);
    }

    public Task<bool> DeleteAnnotationSchemaAsync(string organization, string project, string view, string annotationKey)
    {
        var identity = SchemaIdentity.ForAnnotation(view, AnnotationKey.Parse(annotationKey).ToString());
        return DeleteAsync(organization, project, view, identity);
    }

    public Task<ConfigurationSchema?> GetDescendantTypeSchemaAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode)
    {
        var identity = SchemaIdentity.ForDescendant(view, AnnotationKey.Parse(annotationKey).ToString(), NormalizeAnnotationType(descendantTypeCode));
        return ReadCurrentAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema> SetDescendantTypeSchemaAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode,
        JObject schemaContent,
        string author,
        bool force)
    {
        var identity = SchemaIdentity.ForDescendant(view, AnnotationKey.Parse(annotationKey).ToString(), NormalizeAnnotationType(descendantTypeCode));
        return SaveAsync(organization, project, view, identity, schemaContent, author, force);
    }

    public Task<bool> DeleteDescendantTypeSchemaAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode)
    {
        var identity = SchemaIdentity.ForDescendant(view, AnnotationKey.Parse(annotationKey).ToString(), NormalizeAnnotationType(descendantTypeCode));
        return DeleteAsync(organization, project, view, identity);
    }

    public async Task<CombinedConfigurationSchema?> GetCombinedSchemaAsync(string organization, string project, string view, string annotationKey)
    {
        var parsedKey = AnnotationKey.Parse(annotationKey);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey(GetSchemaPartitionKey(project));
        return await ResolveCombinedAsync(repository, partitionKey, view, parsedKey, schemaOverride: null);
    }

    public async Task ValidateContentAsync(string organization, string project, string view, string annotationKey, JObject content)
    {
        var combined = await GetCombinedSchemaAsync(organization, project, view, annotationKey);

        if (combined is null)
        {
            return;
        }

        var errors = await ValidateAgainstSchemaAsync(content, combined.MergedContent);

        if (errors.Count > 0)
        {
            throw new SchemaValidationException(
            [
                new SchemaValidationError
                {
                    AnnotationKey = annotationKey,
                    ViewName = view,
                    Errors = errors,
                },
            ]);
        }
    }

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetSchemaVersionsAsync(string organization, string project, string view, string annotationType)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return ListVersionsAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema?> GetSchemaVersionContentAsync(string organization, string project, string view, string annotationType, uint version)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return ReadVersionAsync(organization, project, identity, version);
    }

    public Task<DiffResult> GetSchemaVersionChangesAsync(string organization, string project, string view, string annotationType, uint version)
    {
        return GetSchemaVersionChangesAsync(organization, project, view, annotationType, version == 0 ? 0 : version - 1, version);
    }

    public Task<DiffResult> GetSchemaVersionChangesAsync(string organization, string project, string view, string annotationType, uint fromVersion, uint toVersion)
    {
        var typeCode = NormalizeAnnotationType(annotationType);
        var identity = SchemaIdentity.ForType(view, typeCode);
        return DiffAsync(
            organization,
            project,
            identity,
            fromVersion,
            toVersion,
            $"Unknown version {fromVersion} of the schema for {typeCode} in view {view}.",
            $"Unknown version {toVersion} of the schema for {typeCode} in view {view}.");
    }

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetAnnotationSchemaVersionsAsync(string organization, string project, string view, string annotationKey)
    {
        var identity = SchemaIdentity.ForAnnotation(view, AnnotationKey.Parse(annotationKey).ToString());
        return ListVersionsAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema?> GetAnnotationSchemaVersionContentAsync(string organization, string project, string view, string annotationKey, uint version)
    {
        var identity = SchemaIdentity.ForAnnotation(view, AnnotationKey.Parse(annotationKey).ToString());
        return ReadVersionAsync(organization, project, identity, version);
    }

    public Task<DiffResult> GetAnnotationSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, uint version)
    {
        return GetAnnotationSchemaVersionChangesAsync(organization, project, view, annotationKey, version == 0 ? 0 : version - 1, version);
    }

    public Task<DiffResult> GetAnnotationSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, uint fromVersion, uint toVersion)
    {
        var key = AnnotationKey.Parse(annotationKey).ToString();
        var identity = SchemaIdentity.ForAnnotation(view, key);
        return DiffAsync(
            organization,
            project,
            identity,
            fromVersion,
            toVersion,
            $"Unknown version {fromVersion} of the schema for {key} in view {view}.",
            $"Unknown version {toVersion} of the schema for {key} in view {view}.");
    }

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetDescendantTypeSchemaVersionsAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode)
    {
        var identity = SchemaIdentity.ForDescendant(view, AnnotationKey.Parse(annotationKey).ToString(), NormalizeAnnotationType(descendantTypeCode));
        return ListVersionsAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema?> GetDescendantTypeSchemaVersionContentAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode,
        uint version)
    {
        var identity = SchemaIdentity.ForDescendant(view, AnnotationKey.Parse(annotationKey).ToString(), NormalizeAnnotationType(descendantTypeCode));
        return ReadVersionAsync(organization, project, identity, version);
    }

    public Task<DiffResult> GetDescendantTypeSchemaVersionChangesAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode,
        uint version)
    {
        return GetDescendantTypeSchemaVersionChangesAsync(organization, project, view, annotationKey, descendantTypeCode, version == 0 ? 0 : version - 1, version);
    }

    public Task<DiffResult> GetDescendantTypeSchemaVersionChangesAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode,
        uint fromVersion,
        uint toVersion)
    {
        var key = AnnotationKey.Parse(annotationKey).ToString();
        var typeCode = NormalizeAnnotationType(descendantTypeCode);
        var identity = SchemaIdentity.ForDescendant(view, key, typeCode);
        return DiffAsync(
            organization,
            project,
            identity,
            fromVersion,
            toVersion,
            $"Unknown version {fromVersion} of the schema for {typeCode} under {key} in view {view}.",
            $"Unknown version {toVersion} of the schema for {typeCode} under {key} in view {view}.");
    }

    internal static JObject DeepMergeSchemas(IReadOnlyList<ConfigurationSchema> schemas)
    {
        if (schemas.Count == 0)
        {
            return new JObject();
        }

        var merged = (JObject)schemas[0].Content.DeepClone();

        for (var i = 1; i < schemas.Count; i++)
        {
            DeepMergeInto(merged, schemas[i].Content);
        }

        return merged;
    }

    // Legacy project-wide ids are not read. A miss on the view-scoped id is a missing schema.
    private async Task<ConfigurationSchema?> ReadCurrentAsync(string organization, string project, SchemaIdentity identity)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey(GetSchemaPartitionKey(project));
        var entity = await ReadCurrentEntityAsync(repository, partitionKey, identity);
        return entity?.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
    }

    private async Task<ConfigurationSchema> SaveAsync(
        string organization,
        string project,
        string view,
        SchemaIdentity identity,
        JObject schemaContent,
        string author,
        bool force)
    {
        await EnsureValidSchemaAsync(schemaContent);

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetSchemaPartitionKey(project);
        var partitionKey = new PartitionKey(partitionKeyValue);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await ReadCurrentWithETagAsync(repository, partitionKey, identity);
            var (state, stateEtag) = await ReadStateAsync(repository, partitionKey, identity);

            if (existing is not null && JToken.DeepEquals(existing.Content, schemaContent))
            {
                return existing.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
            }

            var errors = await CollectComplianceErrorsAsync(repository, partitionKey, project, view, identity, schemaContent, author);

            if (errors.Count > 0 && !force)
            {
                throw new SchemaValidationException(errors);
            }

            if (errors.Count > 0)
            {
                foreach (var error in errors)
                {
                    _logger.LogWarning(
                        "Schema compliance bypassed with force for {SchemaId} in {Organization}/{Project}/{View}. {AnnotationKey}: {Errors}",
                        identity.CurrentId,
                        organization,
                        project,
                        view,
                        error.AnnotationKey,
                        string.Join("; ", error.Errors));
                }
            }

            var version = existing is null
                ? (state?.LastVersion ?? 0) + 1
                : existing.Version + 1;

            var updated = new ConfigurationSchemaEntity
            {
                PartitionKey = partitionKeyValue,
                Id = identity.CurrentId,
                AnnotationKey = identity.AnnotationKey,
                Name = identity.Name,
                ProjectName = project,
                ViewName = view,
                Content = schemaContent,
                Author = author,
                Version = version,
            };

            var batch = repository.Container.CreateTransactionalBatch(partitionKey);

            if (existing is null)
            {
                batch.CreateItem(updated);
            }
            else
            {
                batch.CreateItem(existing.ToHistory(identity.HistoryId(existing.Version)));
                batch.ReplaceItem(existing.Id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
            }

            AddStateWrite(batch, updated, identity, state, stateEtag, version);

            if (await TryExecuteWriteAsync(batch, identity.CurrentId) is null)
            {
                continue;
            }

            return updated.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
        }

        throw new SchemaConcurrencyException(project, view, identity.CurrentId, MaxRetries);
    }

    private async Task<bool> DeleteAsync(string organization, string project, string view, SchemaIdentity identity)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetSchemaPartitionKey(project);
        var partitionKey = new PartitionKey(partitionKeyValue);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await ReadCurrentWithETagAsync(repository, partitionKey, identity);
            var (state, stateEtag) = await ReadStateAsync(repository, partitionKey, identity);

            if (existing is null)
            {
                return false;
            }

            var batch = repository.Container.CreateTransactionalBatch(partitionKey);
            batch.CreateItem(existing.ToHistory(identity.HistoryId(existing.Version)));
            batch.DeleteItem(identity.CurrentId, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
            AddStateWrite(batch, existing, identity, state, stateEtag, existing.Version);

            if (await TryExecuteWriteAsync(batch, identity.CurrentId) is null)
            {
                continue;
            }

            return true;
        }

        throw new SchemaConcurrencyException(project, view, identity.CurrentId, MaxRetries);
    }

    private async Task<IReadOnlyCollection<ConfigurationVersion>> ListVersionsAsync(string organization, string project, SchemaIdentity identity)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey(GetSchemaPartitionKey(project));
        var feed = repository.Container.GetItemLinqQueryable<ConfigurationSchemaHistoryEntity>(
                requestOptions: new QueryRequestOptions { PartitionKey = partitionKey })
            .Where(history => history.Id.StartsWith(identity.HistoryPrefix))
            .OrderBy(history => history.Version)
            .ToFeedIterator();

        var versions = new List<ConfigurationVersion>();

        while (feed.HasMoreResults)
        {
            var results = await feed.ReadNextAsync();
            versions.AddRange(results.Select(entity => entity.ToConfigurationVersion()));
        }

        var current = await ReadCurrentEntityAsync(repository, partitionKey, identity);

        if (current is not null)
        {
            versions.Add(current.ToConfigurationVersion());
        }

        return versions;
    }

    private async Task<ConfigurationSchema?> ReadVersionAsync(string organization, string project, SchemaIdentity identity, uint version)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey(GetSchemaPartitionKey(project));
        var history = await repository.Container.TryReadItemAsync(
            identity.HistoryId(version),
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaHistoryEntity>(_serializerOptions));

        if (history is not null)
        {
            return history.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
        }

        var current = await ReadCurrentEntityAsync(repository, partitionKey, identity);

        if (current is null || current.Version != version)
        {
            return null;
        }

        return current.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
    }

    private Task<DiffResult> DiffAsync(
        string organization,
        string project,
        SchemaIdentity identity,
        uint fromVersion,
        uint toVersion,
        string fromErrorMessage,
        string toErrorMessage)
    {
        return _differ.GetChangesAsync(
            async () => fromVersion == 0 ? new JObject() : (await ReadVersionAsync(organization, project, identity, fromVersion))?.Content,
            async () => toVersion == 0 ? new JObject() : (await ReadVersionAsync(organization, project, identity, toVersion))?.Content,
            fromErrorMessage,
            toErrorMessage);
    }

    private async Task<IReadOnlyList<SchemaValidationError>> CollectComplianceErrorsAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        string project,
        string view,
        SchemaIdentity identity,
        JObject schemaContent,
        string author)
    {
        var schemaOverride = new SchemaOverride(identity.Kind, identity.ModelType, identity.Kind == SchemaKind.Type ? null : identity.ModelKey, schemaContent, author);
        var errors = new List<SchemaValidationError>();
        var layerCache = new Dictionary<string, ConfigurationSchemaEntity?>();
        var feed = QueryAffectedConfigurations(repository.Container, project, view, identity);

        while (feed.HasMoreResults)
        {
            var results = await feed.ReadNextAsync();

            foreach (var configEntity in results)
            {
                if (!configEntity.Content.HasValues)
                {
                    continue;
                }

                if (identity.Kind == SchemaKind.Descendant
                    && !IsDescendantOf(configEntity.AnnotationKey, identity.ModelKey!, identity.ModelType!))
                {
                    continue;
                }

                var configKey = AnnotationKey.Parse(configEntity.AnnotationKey);
                var combined = await ResolveCombinedAsync(repository, partitionKey, view, configKey, schemaOverride, layerCache);

                if (combined is null)
                {
                    continue;
                }

                var messages = await ValidateAgainstSchemaAsync(configEntity.Content, combined.MergedContent);

                if (messages.Count > 0)
                {
                    errors.Add(new SchemaValidationError
                    {
                        AnnotationKey = configEntity.AnnotationKey,
                        ViewName = configEntity.ViewName,
                        Errors = messages,
                    });
                }
            }
        }

        return errors;
    }

    private async Task<CombinedConfigurationSchema?> ResolveCombinedAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        string view,
        AnnotationKey key,
        SchemaOverride? schemaOverride,
        Dictionary<string, ConfigurationSchemaEntity?>? layerCache = null)
    {
        var applied = new List<ConfigurationSchema>();
        var typeIdentity = SchemaIdentity.ForType(view, key.TypeCode);

        if (schemaOverride is { Kind: SchemaKind.Type } && schemaOverride.TypeCode == key.TypeCode)
        {
            applied.Add(Candidate(view, schemaOverride, key.TypeCode, annotationKey: null));
        }
        else
        {
            var stored = await ReadLayerAsync(repository, partitionKey, typeIdentity, layerCache);

            if (stored is not null)
            {
                applied.Add(stored.ToConfigurationSchema(typeIdentity.ModelType, typeIdentity.ModelKey));
            }
        }

        foreach (var (ancestor, descendantTypeCode) in AnnotationHierarchy.GetAncestorSchemaSources(key))
        {
            var ancestorKey = ancestor.ToString();
            var descendantIdentity = SchemaIdentity.ForDescendant(view, ancestorKey, descendantTypeCode);

            if (schemaOverride is { Kind: SchemaKind.Descendant }
                && schemaOverride.TypeCode == descendantTypeCode
                && schemaOverride.AnnotationKey == ancestorKey)
            {
                applied.Add(Candidate(view, schemaOverride, descendantTypeCode, ancestorKey));
            }
            else
            {
                var stored = await ReadLayerAsync(repository, partitionKey, descendantIdentity, layerCache);

                if (stored is not null)
                {
                    applied.Add(stored.ToConfigurationSchema(descendantIdentity.ModelType, descendantIdentity.ModelKey));
                }
            }
        }

        var annotationIdentity = SchemaIdentity.ForAnnotation(view, key.ToString());

        if (schemaOverride is { Kind: SchemaKind.Annotation } && schemaOverride.AnnotationKey == key.ToString())
        {
            applied.Add(Candidate(view, schemaOverride, annotationType: null, key.ToString()));
        }
        else
        {
            var stored = await ReadLayerAsync(repository, partitionKey, annotationIdentity, layerCache);

            if (stored is not null)
            {
                applied.Add(stored.ToConfigurationSchema(annotationIdentity.ModelType, annotationIdentity.ModelKey));
            }
        }

        if (applied.Count == 0)
        {
            return null;
        }

        return new CombinedConfigurationSchema
        {
            View = view,
            AnnotationKey = key.ToString(),
            MergedContent = DeepMergeSchemas(applied),
            AppliedSchemas = applied,
        };
    }

    private Task<(ConfigurationSchemaEntity? Item, string? ETag)> ReadCurrentWithETagAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        SchemaIdentity identity)
    {
        return repository.Container.TryReadItemWithETagAsync(
            identity.CurrentId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaEntity>(_serializerOptions));
    }

    private async Task<ConfigurationSchemaEntity?> ReadLayerAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        SchemaIdentity identity,
        Dictionary<string, ConfigurationSchemaEntity?>? layerCache)
    {
        if (layerCache is not null && layerCache.TryGetValue(identity.CurrentId, out var cached))
        {
            return cached;
        }

        var stored = await ReadCurrentEntityAsync(repository, partitionKey, identity);

        if (layerCache is not null)
        {
            layerCache[identity.CurrentId] = stored;
        }

        return stored;
    }

    private async Task<ConfigurationSchemaEntity?> ReadCurrentEntityAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        SchemaIdentity identity)
    {
        return await repository.Container.TryReadItemAsync(
            identity.CurrentId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaEntity>(_serializerOptions));
    }

    private Task<(ConfigurationSchemaStateEntity? Item, string? ETag)> ReadStateAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        SchemaIdentity identity)
    {
        return repository.Container.TryReadItemWithETagAsync(
            identity.StateId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaStateEntity>(_serializerOptions));
    }

    private static FeedIterator<ConfigurationEntity> QueryAffectedConfigurations(Container container, string project, string view, SchemaIdentity identity)
    {
        var query = container.GetItemLinqQueryable<ConfigurationEntity>(allowSynchronousQueryExecution: false);

        if (identity.Kind == SchemaKind.Annotation)
        {
            var id = $"{view}.{EntityIdPrefixTypes.Configuration}.{identity.AnnotationKey}";
            return query
                .Where(config => config.ProjectName == project && config.Id == id)
                .ToFeedIterator();
        }

        var prefix = $"{view}.{EntityIdPrefixTypes.Configuration}.{identity.ModelType}.";
        return query
            .Where(config =>
                config.ProjectName == project
                && config.ViewName == view
                && config.Id.StartsWith(prefix))
            .ToFeedIterator();
    }

    private static void AddStateWrite(
        TransactionalBatch batch,
        ConfigurationSchemaEntity schema,
        SchemaIdentity identity,
        ConfigurationSchemaStateEntity? state,
        string? stateEtag,
        ulong version)
    {
        var newState = new ConfigurationSchemaStateEntity
        {
            PartitionKey = schema.PartitionKey,
            Id = identity.StateId,
            AnnotationKey = schema.AnnotationKey,
            Name = schema.Name,
            ProjectName = schema.ProjectName,
            ViewName = schema.ViewName,
            LastVersion = Math.Max(state?.LastVersion ?? 0, version),
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

    private static async Task<string?> TryExecuteWriteAsync(TransactionalBatch batch, string id)
    {
        using var response = await batch.ExecuteAsync();

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ConfigurationStorageException(
                $"Failed to write schema '{id}' with status {response.StatusCode}: {response.ErrorMessage}",
                response.StatusCode);
        }

        return response[response.Count - 1].ETag;
    }

    private static async Task EnsureValidSchemaAsync(JObject schemaContent)
    {
        try
        {
            await JsonSchema.FromJsonAsync(schemaContent.ToString(Formatting.None));
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid JSON Schema: {ex.Message}", ex);
        }
    }

    private static async Task<IReadOnlyList<string>> ValidateAgainstSchemaAsync(JObject content, JObject schemaContent)
    {
        JsonSchema schema;

        try
        {
            schema = await JsonSchema.FromJsonAsync(schemaContent.ToString(Formatting.None));
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid JSON Schema: {ex.Message}", ex);
        }

        var validationResults = schema.Validate(content.ToString(Formatting.None));

        if (validationResults.Count == 0)
        {
            return [];
        }

        return validationResults.Select(error => $"{error.Path}: {error.Kind}").ToList();
    }

    private static bool IsDescendantOf(string annotationKey, string ancestorKey, string descendantTypeCode)
    {
        var ancestors = AnnotationHierarchy.GetAncestorSchemaSources(AnnotationKey.Parse(annotationKey));
        return ancestors.Any(ancestor => ancestor.Ancestor == ancestorKey && ancestor.DescendantTypeCode == descendantTypeCode);
    }

    private static ConfigurationSchema Candidate(string view, SchemaOverride schemaOverride, string? annotationType, string? annotationKey)
    {
        return new ConfigurationSchema
        {
            View = view,
            AnnotationType = annotationType,
            AnnotationKey = annotationKey,
            Version = 0,
            Content = schemaOverride.Content,
            Author = schemaOverride.Author,
        };
    }

    private static void DeepMergeInto(JObject target, JObject source)
    {
        foreach (var property in source.Properties())
        {
            var existing = target.Property(property.Name);

            if (existing is null)
            {
                target.Add(property.Name, property.Value.DeepClone());
                continue;
            }

            if (property.Name == "required"
                && existing.Value is JArray existingArray
                && property.Value is JArray sourceArray)
            {
                var existingValues = existingArray.Select(token => token.ToString()).ToHashSet();

                foreach (var item in sourceArray)
                {
                    if (existingValues.Add(item.ToString()))
                    {
                        existingArray.Add(item.DeepClone());
                    }
                }

                continue;
            }

            if (existing.Value is JObject existingObject && property.Value is JObject sourceObject)
            {
                DeepMergeInto(existingObject, sourceObject);
                continue;
            }

            existing.Value = property.Value.DeepClone();
        }
    }

    private static string GetSchemaPartitionKey(string project)
    {
        return $"{project}.{SchemaPartitionSuffix}";
    }

    private static string NormalizeAnnotationType(string annotationType)
    {
        var normalized = annotationType.ToLowerInvariant();

        return AnnotationTypeCodes.ValidCodes.ContainsKey(normalized)
            ? normalized
            : throw new ArgumentException($"Invalid annotation type code: {annotationType}");
    }

    // The id suffix after "{view}.cfs." is "t.{type}", "a.{annotationKey}" or "d.{type}.{ancestorKey}" (SchemaIdentity).
    private static ConfigurationSchemaSummary? ToSchemaSummary(DocumentListingRow row, string suffix)
    {
        var parts = suffix.Split('.', 3);
        (ConfigurationSchemaKind Kind, string? Type, string? Key)? identity = parts switch
        {
            ["t", var type] => (ConfigurationSchemaKind.Type, type, null),
            ["a", var first, var rest] => (ConfigurationSchemaKind.Annotation, null, $"{first}.{rest}"),
            ["d", var type, var key] => (ConfigurationSchemaKind.DescendantType, type, key),
            _ => null,
        };

        return identity is { } value
            ? new ConfigurationSchemaSummary
            {
                Kind = value.Kind,
                AnnotationType = value.Type,
                AnnotationKey = value.Key,
                Version = row.Version,
                Author = row.Author,
                UpdatedAt = ListingTimestamps.ToUpdatedAt(row.Timestamp),
            }
            : null;
    }

    private readonly record struct SchemaIdentity(
        SchemaKind Kind,
        string View,
        string Suffix,
        string AnnotationKey,
        string Name,
        string? ModelType,
        string? ModelKey)
    {
        public string CurrentId => $"{View}.{EntityIdPrefixTypes.ConfigurationSchema}.{Suffix}";

        public string StateId => $"{View}.{EntityIdPrefixTypes.ConfigurationSchemaState}.{Suffix}";

        public string HistoryPrefix => $"{View}.{EntityIdPrefixTypes.ConfigurationSchemaVersion}.{Suffix}.";

        public string HistoryId(ulong version) => $"{HistoryPrefix}{version}";

        public static SchemaIdentity ForType(string view, string typeCode)
        {
            return new SchemaIdentity(SchemaKind.Type, view, $"t.{typeCode}", typeCode, typeCode, typeCode, null);
        }

        public static SchemaIdentity ForAnnotation(string view, string annotationKey)
        {
            return new SchemaIdentity(SchemaKind.Annotation, view, $"a.{annotationKey}", annotationKey, annotationKey, null, annotationKey);
        }

        public static SchemaIdentity ForDescendant(string view, string ancestorKey, string descendantType)
        {
            return new SchemaIdentity(
                SchemaKind.Descendant,
                view,
                $"d.{descendantType}.{ancestorKey}",
                ancestorKey,
                $"dt.{descendantType}.{ancestorKey}",
                descendantType,
                ancestorKey);
        }
    }

    private sealed record SchemaOverride(SchemaKind Kind, string? TypeCode, string? AnnotationKey, JObject Content, string Author);
}
