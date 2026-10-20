using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;
using _42.Platform.Storyteller.Json;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

public partial class CosmosConfigurationService : IConfigurationService
{
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IConfigurationBindingResolver? _bindingResolver;
    private readonly IConfigurationSchemaService? _schemaService;
    private readonly IJsonSerializationSettingsProvider _jsonSettingsProvider;
    private readonly JsonSerializerSettings _serializerOptions;
    private readonly JsonContentDiffer _differ;
    private readonly ILogger<CosmosConfigurationService> _logger;
    private readonly AnnotationAuditStore? _auditStore;
    private readonly TimeProvider _timeProvider;

    public CosmosConfigurationService(
        IContainerRepositoryProvider repositoryProvider,
        IJsonSerializationSettingsProvider jsonSettingsProvider,
        IOptions<JsonSerializerSettings> serializerOptions,
        IConfigurationBindingResolver bindingResolver = null,
        IConfigurationSchemaService schemaService = null,
        ILogger<CosmosConfigurationService> logger = null,
        AnnotationAuditStore? auditStore = null,
        TimeProvider? timeProvider = null)
    {
        _auditStore = auditStore;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _repositoryProvider = repositoryProvider;
        _bindingResolver = bindingResolver;
        _schemaService = schemaService;
        _jsonSettingsProvider = jsonSettingsProvider;
        _serializerOptions = serializerOptions.Value;
        _differ = new JsonContentDiffer(jsonSettingsProvider);
        _logger = logger ?? NullLogger<CosmosConfigurationService>.Instance;
    }

    public async Task<bool> HasConfigurationContentAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var configurationKey = $"{EntityIdPrefixTypes.Configuration}.{key.Annotation}";
        var id = $"{key.ViewName}.{configurationKey}";
        var partitionKey = key.GetCosmosPartitionKey();
        var configuration = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));
        return configuration is not null
               && configuration.AnnotationDeletionId is null
               && configuration.Content.HasValues;
    }

    public async Task<ConfigurationsResponse> ListConfigurationsAsync(
        string organization,
        string project,
        string view,
        string? annotationType = null,
        string? keyPrefix = null,
        string? continuationToken = null)
    {
        var idPrefix = $"{view}.{EntityIdPrefixTypes.Configuration}.";
        var text = "SELECT c.AnnotationKey, c.Version, c.Modified, c.CalculatedContentHash, "
            + "(IS_OBJECT(c.Content) AND ARRAY_LENGTH(ObjectToArray(c.Content)) > 0) AS HasContent "
            + "FROM c WHERE STARTSWITH(c.PartitionKey, @projectPrefix) AND STARTSWITH(c.id, @idPrefix) "
            + "AND NOT IS_DEFINED(c.AnnotationDeletionId)";

        if (!string.IsNullOrWhiteSpace(annotationType))
        {
            text += " AND STARTSWITH(c.id, @typePrefix)";
        }

        if (!string.IsNullOrWhiteSpace(keyPrefix))
        {
            text += " AND STARTSWITH(c.id, @keyPrefix)";
        }

        var query = new QueryDefinition(text)
            .WithParameter("@projectPrefix", $"{project}.")
            .WithParameter("@idPrefix", idPrefix)
            .WithParameter("@typePrefix", $"{idPrefix}{annotationType?.Trim().ToLowerInvariant()}.")
            .WithParameter("@keyPrefix", $"{idPrefix}{keyPrefix?.Trim()}");

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        using var iterator = repository.Container.GetItemQueryIterator<ConfigurationListingRow>(
            query,
            string.IsNullOrWhiteSpace(continuationToken) ? null : continuationToken,
            new QueryRequestOptions { MaxItemCount = CosmosConstants.MaxItemCountPerPage });

        if (!iterator.HasMoreResults)
        {
            return new ConfigurationsResponse { Configurations = [] };
        }

        var page = await iterator.ReadNextAsync();
        var configurations = page
            .Where(row => !string.IsNullOrEmpty(row.AnnotationKey))
            .Select(row => (Row: row, Code: row.AnnotationKey!.Split('.', 2)[0]))
            .Where(item => AnnotationTypeCodes.ValidCodes.ContainsKey(item.Code))
            .Select(item => new ConfigurationSummary
            {
                AnnotationKey = item.Row.AnnotationKey!,
                AnnotationType = AnnotationTypeCodes.ValidCodes[item.Code],
                Version = item.Row.Version,
                Modified = item.Row.Modified,
                Hash = item.Row.CalculatedContentHash,
                HasContent = item.Row.HasContent,
            })
            .ToList();

        return new ConfigurationsResponse
        {
            Configurations = configurations,
            ContinuationToken = page.ContinuationToken,
            Count = configurations.Count,
        };
    }

    public async Task<Configuration?> GetRawConfigurationAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var configurationKey = $"{EntityIdPrefixTypes.Configuration}.{key.Annotation}";
        var id = $"{key.ViewName}.{configurationKey}";
        var partitionKey = key.GetCosmosPartitionKey();

        var configuration = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        // the configuration of a soft-deleted annotation is hidden until a restore
        if (configuration?.AnnotationDeletionId is not null)
        {
            return null;
        }

        if (configuration is null
            && await AnnotationItemStore.ReadStateAsync(repository.Container, key) != AnnotationItemState.Active)
        {
            return null;
        }

        if (configuration?.CalculatedContent is not null)
        {
            return configuration.ToConfiguration();
        }

        var node = BuildInheritanceGraph(key);
        var calculatedConfig = await CalculateAndCacheConfigurationAsync(node, repository, new Dictionary<string, TemplateSnapshot>());

        if (calculatedConfig is null)
        {
            return null;
        }

        // reload from database to get the latest state with hash
        configuration = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        return configuration is not null
            ? configuration.ToConfiguration(calculatedConfig)
            : calculatedConfig.ToConfiguration(key.Annotation, _timeProvider.GetUtcNow());
    }

    public Task<Configuration?> GetResolvedConfigurationAsync(FullKey key, bool includeSecrets = false)
    {
        return GetResolvedConfigurationInternalAsync(key, includeSecrets);
    }

    public Task<Configuration?> GetResolvedConfigurationWithSecretsAsync(FullKey key)
    {
        return GetResolvedConfigurationInternalAsync(key, true);
    }

    public Task<Configuration?> GetResolvedConfigurationWithoutSecretsAsync(FullKey key)
    {
        return GetResolvedConfigurationInternalAsync(key, false);
    }

    public bool HasBindings(Configuration effective)
    {
        ArgumentNullException.ThrowIfNull(effective);
        return _bindingResolver?.ContainsBindings(effective.Content) ?? false;
    }

    public async Task<Configuration> ResolveConfigurationAsync(FullKey key, Configuration effective, bool includeSecrets = false)
    {
        ArgumentNullException.ThrowIfNull(effective);

        if (_bindingResolver is null)
        {
            return effective;
        }

        var scope = new BindingScope
        {
            Document = effective.Content.DeepClone(),
            Context = new ConfigurationBindingContext(key),
        };

        await _bindingResolver.ResolveAsync(effective.Content, includeSecrets, scope);
        return effective;
    }

    public async Task<Configuration?> GetConfigurationHierarchyViewAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        if (await AnnotationItemStore.ReadStateAsync(repository.Container, key) != AnnotationItemState.Active)
        {
            return null;
        }

        var node = BuildInheritanceGraph(key);
        var result = new JObject();
        await CollectHierarchyNodesAsync(node, repository, result, new HashSet<string>());

        // reload from database to get the latest state with hash
        var configurationKey = $"{EntityIdPrefixTypes.Configuration}.{key.Annotation}";
        var id = $"{key.ViewName}.{configurationKey}";
        var partitionKey = key.GetCosmosPartitionKey();
        var configuration = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        return configuration is not null
            ? configuration.ToConfiguration(result)
            : result.ToConfiguration(key.Annotation, _timeProvider.GetUtcNow());
    }

    public async Task<IReadOnlyCollection<ConfigurationVersion>> GetConfigurationVersionsAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);

        // the versions of a soft-deleted annotation are hidden until a restore
        if (await AnnotationItemStore.ReadStateAsync(repository.Container, key) == AnnotationItemState.Deleted)
        {
            return [];
        }

        var annotationKey = key.Annotation.ToString();
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);

        // TODO: [P3] optimize the query to ignore Content
        var feed = repository.Container.GetItemLinqQueryable<ConfigurationHistoryEntity>(
                requestOptions: new QueryRequestOptions { PartitionKey = partitionKey })
            .Where(history => history.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.ConfigurationVersion}.{annotationKey}."))
            .OrderBy(history => history.Version)
            .ToFeedIterator();

        var versions = new List<ConfigurationVersion>();
        while (feed.HasMoreResults)
        {
            var results = await feed.ReadNextAsync();
            versions.AddRange(results.Select(entity => entity.ToConfigurationVersion()));
        }

        var configurationKey = $"{EntityIdPrefixTypes.Configuration}.{key.Annotation}";
        var configurationId = $"{key.ViewName}.{configurationKey}";

        var configuration = await repository.Container.TryReadItemAsync(
            configurationId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        if (configuration is not null)
        {
            versions.Add(configuration.ToConfigurationVersion());
        }

        return versions;
    }

    public async Task<Configuration?> GetConfigurationVersionContentAsync(FullKey key, uint version)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);

        if (await AnnotationItemStore.ReadStateAsync(repository.Container, key) == AnnotationItemState.Deleted)
        {
            return null;
        }

        var annotationKey = key.Annotation.ToString();
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);
        var configVersionId = $"{key.ViewName}.{EntityIdPrefixTypes.ConfigurationVersion}.{annotationKey}.{version}";
        var versionEntity = await repository.Container.TryReadItemAsync(
            configVersionId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationHistoryEntity>(_serializerOptions));

        if (versionEntity is not null)
        {
            return versionEntity.ToConfiguration();
        }

        var configId = $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{annotationKey}";
        var configEntity = await repository.Container.TryReadItemAsync(
            configId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        if (configEntity is null
            || configEntity.Version != version)
        {
            return null;
        }

        return configEntity.ToConfigurationFromContent();
    }

    public Task<DiffResult> GetConfigurationVersionChangesAsync(FullKey key, uint version)
    {
        return GetConfigurationVersionChangesAsync(key, version == 0 ? 0 : version - 1, version);
    }

    public Task<DiffResult> GetConfigurationVersionChangesAsync(FullKey key, uint fromVersion, uint toVersion)
    {
        return _differ.GetChangesAsync(
            async () => fromVersion == 0 ? new JObject() : (await GetConfigurationVersionContentAsync(key, fromVersion))?.Content,
            async () => toVersion == 0 ? new JObject() : (await GetConfigurationVersionContentAsync(key, toVersion))?.Content,
            $"Unknown version {fromVersion} of the configuration for {key.Annotation}.",
            $"Unknown version {toVersion} of the configuration for {key.Annotation}.");
    }

    public Task<DiffResult> GetConfigurationViewChangesAsync(FullKey sourceKey, string toView)
    {
        var targetKey = FullKey.Create(sourceKey.Annotation, sourceKey.OrganizationName, sourceKey.ProjectName, toView);
        return _differ.GetChangesAsync(
            async () => (await GetRawConfigurationAsync(sourceKey))?.Content,
            async () => (await GetRawConfigurationAsync(targetKey))?.Content,
            $"Unknown configuration for {sourceKey.Annotation} in view {sourceKey.ViewName}.",
            $"Unknown configuration for {sourceKey.Annotation} in view {toView}.");
    }

    public Task<Configuration> CreateOrUpdateConfigurationAsync(FullKey key, JObject value, AuditActor actor, bool force = false)
    {
        return WriteConfigurationAsync(key, value, actor, force, merge: true);
    }

    // Merges the value into the stored document, or replaces the document with it (an import).
    private async Task<Configuration> WriteConfigurationAsync(FullKey key, JObject value, AuditActor actor, bool force, bool merge)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var annotationKey = key.Annotation.ToString();
        var configurationKey = $"{EntityIdPrefixTypes.Configuration}.{annotationKey}";
        var id = $"{key.ViewName}.{configurationKey}";
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);

        var existingConfiguration = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        await EnsureAnnotationNotDeletedAsync(key, repository, existingConfiguration);

        if (existingConfiguration is null)
        {
            // create a new configuration if there is nothing yet
            value = value.RemoveRequested();
            value = await value.ApplyPatchRequested();

            if (!force && _schemaService is not null && value.HasValues)
            {
                await _schemaService.ValidateContentAsync(key.OrganizationName, key.ProjectName, key.ViewName, annotationKey, value);
            }

            var highestArchivedVersion = await GetHighestArchivedVersionAsync(key, repository);

            var configuration = new ConfigurationEntity
            {
                PartitionKey = partitionKeyValue,
                Id = id,
                AnnotationKey = annotationKey,
                IsServerSubstitutionDisabled = false,
                Name = key.Annotation.Name,
                ProjectName = key.ProjectName,
                ViewName = key.ViewName,
                Content = value,
                Version = highestArchivedVersion + 1,
                Modified = AuditStamp.Of(actor, _timeProvider.GetUtcNow()),
            };

            await repository.Container.CreateItemAsync(configuration, partitionKey);

            // invalidate the cached calculations of all descendants once the document exists, so a calculation
            // that read it as missing is either cleared here or fails to cache on the changed ETag
            await InvalidateConfigurationsAsync(key, repository);
            await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationChanged, configuration.Version);
            return configuration.ToConfiguration();
        }

        var newContent = value;

        // merge into stored content when there is some; an empty document is replaced
        if (merge && existingConfiguration.Content.HasValues)
        {
            newContent = (JObject)existingConfiguration.Content.DeepClone();
            newContent.MergeInto(value);
            newContent = newContent.RemoveRequested();
            newContent = await newContent.ApplyPatchRequested();

            if (JToken.DeepEquals(existingConfiguration.Content, newContent))
            {
                // check for no change after merge
                return existingConfiguration.ToConfigurationFromContent();
            }
        }
        else
        {
            newContent = newContent.RemoveRequested();
            newContent = await newContent.ApplyPatchRequested();

            if (!merge
                && existingConfiguration.Content.HasValues
                && JToken.DeepEquals(existingConfiguration.Content, newContent))
            {
                // a replacement by an equal document writes nothing
                return existingConfiguration.ToConfigurationFromContent();
            }
        }

        // validate before any write, so a rejection cannot leave a history item
        if (!force && _schemaService is not null && newContent.HasValues)
        {
            await _schemaService.ValidateContentAsync(key.OrganizationName, key.ProjectName, key.ViewName, annotationKey, newContent);
        }

        if (existingConfiguration.Content.HasValues)
        {
            var historyVersion = existingConfiguration.Version;
            var historyKey = $"{EntityIdPrefixTypes.ConfigurationVersion}.{annotationKey}.{historyVersion}";
            var historyId = $"{key.ViewName}.{historyKey}";
            var history = new ConfigurationHistoryEntity
            {
                PartitionKey = partitionKeyValue,
                Id = historyId,
                AnnotationKey = annotationKey,
                Version = historyVersion,
                Name = existingConfiguration.Name,
                ProjectName = existingConfiguration.ProjectName,
                ViewName = existingConfiguration.ViewName,
                Content = existingConfiguration.Content,
                Modified = existingConfiguration.Modified,
            };

            await repository.Container.CreateItemAsync(history, partitionKey);
        }

        // an item that only cached the effective document (version 0) continues after the archived versions,
        // which a clear left behind, so the next archive cannot collide with them
        var previousVersion = existingConfiguration.Content.HasValues
            ? existingConfiguration.Version
            : Math.Max(existingConfiguration.Version, await GetHighestArchivedVersionAsync(key, repository));

        // invalidate the cached calculations of all descendants
        await InvalidateConfigurationsAsync(key, repository);

        // save a new version of the configuration
        var newConfiguration = existingConfiguration with
        {
            Version = previousVersion + 1,
            Content = newContent,
            Modified = AuditStamp.Of(actor, _timeProvider.GetUtcNow()),
            CalculatedContent = null,
            CalculatedContentHash = null,
        };
        await repository.Container.UpsertItemAsync(newConfiguration, partitionKey);
        await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationChanged, newConfiguration.Version);

        return newConfiguration.ToConfigurationFromContent();
    }

    public async Task<Configuration> PatchConfigurationAsync(FullKey key, JArray patchOperations, AuditActor actor, bool force = false)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var annotationKey = key.Annotation.ToString();
        var configurationKey = $"{EntityIdPrefixTypes.Configuration}.{annotationKey}";
        var id = $"{key.ViewName}.{configurationKey}";
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);

        var existingConfiguration = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        await EnsureAnnotationNotDeletedAsync(key, repository, existingConfiguration);

        if (existingConfiguration is null)
        {
            throw new ConfigurationNotFoundException(key.Annotation);
        }

        var newContent = await existingConfiguration.Content.ApplyPatch(patchOperations);

        if (JToken.DeepEquals(existingConfiguration.Content, newContent))
        {
            return existingConfiguration.ToConfigurationFromContent();
        }

        if (!force && _schemaService is not null && newContent.HasValues)
        {
            await _schemaService.ValidateContentAsync(key.OrganizationName, key.ProjectName, key.ViewName, annotationKey, newContent);
        }

        // invalidate all dependent configurations before mutating this partition
        await InvalidateConfigurationsAsync(key, repository);

        // save history of the previous version together with the next version atomically
        var historyVersion = existingConfiguration.Version;
        var historyKey = $"{EntityIdPrefixTypes.ConfigurationVersion}.{annotationKey}.{historyVersion}";
        var historyId = $"{key.ViewName}.{historyKey}";
        var history = new ConfigurationHistoryEntity
        {
            PartitionKey = partitionKeyValue,
            Id = historyId,
            AnnotationKey = annotationKey,
            Version = historyVersion,
            Name = existingConfiguration.Name,
            ProjectName = existingConfiguration.ProjectName,
            ViewName = existingConfiguration.ViewName,
            Content = existingConfiguration.Content,
            Modified = existingConfiguration.Modified,
        };

        var newConfiguration = existingConfiguration with
        {
            Version = existingConfiguration.Version + 1,
            Content = newContent,
            Modified = AuditStamp.Of(actor, _timeProvider.GetUtcNow()),
            CalculatedContent = null,
            CalculatedContentHash = null,
        };

        var batch = repository.Container.CreateTransactionalBatch(partitionKey);
        batch.CreateItem(history);
        batch.UpsertItem(newConfiguration);
        using var response = await batch.ExecuteAsync();

        // the version was already archived by a concurrent writer (409); the read can't guard the upsert by ETag,
        // because invalidating the dependent configurations above may rewrite this document too
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new ConflictException($"The configuration '{annotationKey}' was changed concurrently, try again.", ErrorCodes.Conflict);
        }

        if (!response.IsSuccessStatusCode)
        {
            // the database details stay in the log, the caller gets only the status
            _logger.LogError(
                "Patching the configuration {AnnotationKey} in {PartitionKey} failed with {StatusCode} (activity {ActivityId}): {ErrorMessage}",
                annotationKey,
                partitionKeyValue,
                response.StatusCode,
                response.ActivityId,
                response.ErrorMessage);
            throw new ConfigurationStorageException(
                $"Failed to patch configuration '{annotationKey}' with status {response.StatusCode}.",
                response.StatusCode);
        }

        await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationChanged, newConfiguration.Version);
        return newConfiguration.ToConfigurationFromContent();
    }

    public async Task ClearConfigurationAsync(FullKey key, AuditActor? actor = null)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var annotationKey = key.Annotation.ToString();
        var configurationKey = $"{EntityIdPrefixTypes.Configuration}.{annotationKey}";
        var id = $"{key.ViewName}.{configurationKey}";
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);

        var existingConfiguration = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        await EnsureAnnotationNotDeletedAsync(key, repository, existingConfiguration);

        if (existingConfiguration is null
            || !existingConfiguration.Content.HasValues)
        {
            // if there is no entity or there is no configuration content (only pre-calculated content)
            return;
        }

        var historyVersion = existingConfiguration.Version;
        var historyKey = $"{EntityIdPrefixTypes.ConfigurationVersion}.{annotationKey}.{historyVersion}";
        var historyId = $"{key.ViewName}.{historyKey}";
        var history = new ConfigurationHistoryEntity
        {
            PartitionKey = partitionKeyValue,
            Id = historyId,
            AnnotationKey = annotationKey,
            Version = historyVersion,
            Name = existingConfiguration.Name,
            ProjectName = existingConfiguration.ProjectName,
            ViewName = existingConfiguration.ViewName,
            Content = existingConfiguration.Content,
            Modified = existingConfiguration.Modified,
        };

        var transaction = repository.Container.CreateTransactionalBatch(partitionKey);
        transaction.CreateItem(history);
        transaction.DeleteItem(id);
        await transaction.ExecuteAsync();
        await InvalidateConfigurationsAsync(key, repository);
        await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationDeleted, historyVersion);
    }

    public async Task DeleteAsync(FullKey key, AuditActor? actor = null)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var existingConfiguration = await repository.Container.TryReadItemAsync(
            $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{key.Annotation}",
            key.GetCosmosPartitionKey(),
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        await EnsureAnnotationNotDeletedAsync(key, repository, existingConfiguration);
        await ForceDeleteConfigurationAsync(key, repository);
        await InvalidateConfigurationsAsync(key, repository);

        if (existingConfiguration?.Content.HasValues == true)
        {
            await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationDeleted, existingConfiguration.Version);
        }
    }

    public async Task<IReadOnlyList<ConfigurationHistory>> GetConfigurationHistoriesAsync(
        string organization,
        string project,
        string view,
        CancellationToken cancellationToken = default)
    {
        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        var deletedKeys = await ReadDeletedAnnotationKeysAsync(container, project, view, cancellationToken);
        var currentPrefix = $"{view}.{EntityIdPrefixTypes.Configuration}.";
        var query = new QueryDefinition(
                "SELECT c.id, c.AnnotationKey, c.Version, c.Content, c.Modified, c.AnnotationDeletionId FROM c "
                + "WHERE STARTSWITH(c.PartitionKey, @projectPrefix) AND (STARTSWITH(c.id, @currentPrefix) OR STARTSWITH(c.id, @historyPrefix))")
            .WithParameter("@projectPrefix", $"{project}.")
            .WithParameter("@currentPrefix", currentPrefix)
            .WithParameter("@historyPrefix", $"{view}.{EntityIdPrefixTypes.ConfigurationVersion}.");

        var histories = new DocumentHistories<string>(StringComparer.Ordinal);
        using var iterator = container.GetItemQueryStreamIterator(query);

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            var page = response.Content.DeserializeNewtonsoft<QueryPage<DocumentArchiveRow>>(_serializerOptions);

            foreach (var row in page?.Documents ?? [])
            {
                if (row.Id is null
                    || row.AnnotationKey is null
                    || deletedKeys.Contains(row.AnnotationKey)
                    || !AnnotationKey.TryParse(row.AnnotationKey, out _))
                {
                    // a deleted annotation hides its versions
                    continue;
                }

                var isCurrent = row.Id.StartsWith(currentPrefix, StringComparison.Ordinal);

                if (isCurrent && row.AnnotationDeletionId is not null)
                {
                    continue;
                }

                histories.Add(row.AnnotationKey, row, isCurrent);
            }
        }

        return histories.Build()
            .OrderBy(history => history.Identity, StringComparer.Ordinal)
            .Select(history => new ConfigurationHistory { AnnotationKey = history.Identity, Versions = history.Versions, Current = history.Current })
            .ToList();
    }

    public async Task<ViewImportResult> ImportConfigurationAsync(
        FullKey key,
        JObject content,
        ViewImportOptions options,
        ViewImportOverlay overlay)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var annotationKey = key.Annotation.ToString();

        // an annotation the import makes active counts as active, which matters in a dry run, where it is not written
        if (!overlay.ActiveAnnotations.Contains(annotationKey))
        {
            var state = await AnnotationItemStore.ReadStateAsync(repository.Container, key);

            if (state != AnnotationItemState.Active)
            {
                if (overlay.FailedAnnotations.Contains(annotationKey))
                {
                    return new ViewImportResult(
                        ViewImportOutcome.Skipped,
                        ErrorCode: ErrorCodes.DependencyFailed,
                        Message: $"The annotation {annotationKey} failed in this import.");
                }

                return state == AnnotationItemState.Deleted
                    ? DeletedImport(annotationKey)
                    : new ViewImportResult(
                        ViewImportOutcome.Skipped,
                        ErrorCode: ErrorCodes.AnnotationNotFound,
                        Message: $"The view {key.ViewName} has no annotation {annotationKey}.");
            }
        }

        JObject document;

        try
        {
            // the directives apply to the document alone, as on a create
            document = await ((JObject)content.DeepClone()).RemoveRequested().ApplyPatchRequested();
        }
        catch (JsonPatchException exception)
        {
            return new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: exception.ErrorCode, Message: exception.Message);
        }

        if (!document.HasValues)
        {
            return new ViewImportResult(
                ViewImportOutcome.Skipped,
                ErrorCode: ErrorCodes.EmptyDocument,
                Message: "An empty document is not imported; delete the configuration to clear it.");
        }

        var existing = await repository.Container.TryReadItemAsync(
            $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{annotationKey}",
            key.GetCosmosPartitionKey(),
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        // the re-create of the annotation empties its configuration (in a dry run, it is still stored), so the file
        // starts the new lifetime as the next version
        var hasContent = existing?.Content.HasValues == true && !overlay.RecreatedAnnotations.Contains(annotationKey);

        if (hasContent && JToken.DeepEquals(existing!.Content, document))
        {
            return new ViewImportResult(ViewImportOutcome.Unchanged, existing.Version);
        }

        if (hasContent && options.Existing == ViewImportExisting.Skip)
        {
            return new ViewImportResult(
                ViewImportOutcome.Skipped,
                ErrorCode: ErrorCodes.DocumentExists,
                Message: $"The view has a different configuration of {annotationKey}, and Skip keeps it.");
        }

        IReadOnlyList<ViewImportWarning>? warnings = null;

        if (_schemaService is not null)
        {
            try
            {
                // a real run has written the schemas of the archive already; a dry run takes them from the overlay
                await _schemaService.ValidateContentAsync(
                    key.OrganizationName,
                    key.ProjectName,
                    key.ViewName,
                    annotationKey,
                    document,
                    options.DryRun ? overlay : null);
            }
            catch (SchemaValidationException exception)
            {
                var errors = string.Join("; ", exception.ValidationErrors.SelectMany(error => error.Errors));

                if (!options.Force)
                {
                    return new ViewImportResult(
                        ViewImportOutcome.Failed,
                        ErrorCode: ErrorCodes.SchemaViolation,
                        Message: $"The document violates its schema: {errors}");
                }

                warnings =
                [
                    new ViewImportWarning
                    {
                        Code = ErrorCodes.SchemaViolation,
                        Message = $"Force stores the document although it violates its schema: {errors}",
                    },
                ];
            }
        }

        var outcome = hasContent ? ViewImportOutcome.Updated : ViewImportOutcome.Created;

        if (options.DryRun)
        {
            return new ViewImportResult(outcome, Warnings: warnings);
        }

        try
        {
            // validated above, so the write skips the schema
            var written = await WriteConfigurationAsync(key, document, options.Actor, force: true, merge: false);
            return new ViewImportResult(outcome, written.Version, Warnings: warnings);
        }
        catch (ConflictException exception) when (exception.ErrorCode == ErrorCodes.AnnotationDeleted)
        {
            // deleted between the check and the write
            return DeletedImport(annotationKey);
        }
    }

    private async Task<Configuration?> GetResolvedConfigurationInternalAsync(FullKey key, bool includeSecrets)
    {
        var config = await GetRawConfigurationAsync(key);

        if (config is null)
        {
            return null;
        }

        return await ResolveConfigurationAsync(key, config, includeSecrets);
    }

    private async Task TryAutogeneratePropertiesAsync(
        JObject config,
        FullKey key,
        IContainerRepository repository,
        Dictionary<string, TemplateSnapshot> templates)
    {
        var typeCode = key.Annotation.TypeCode.ToLowerInvariant();

        // the whole inheritance graph is in one view, so the view template is read only once per calculation
        if (!templates.TryGetValue(typeCode, out var template))
        {
            var (typeTemplateEntity, etag) = await ReadTemplateAsync(key, typeCode, repository);
            template = new TemplateSnapshot(typeTemplateEntity?.Content, etag);
            templates[typeCode] = template;
        }

        if (template.Content is null)
        {
            return;
        }

        config.MergeInto(template.Content);
    }

    private Task<(GenerateTemplateEntity? Item, string? ETag)> ReadTemplateAsync(FullKey key, string typeCode, IContainerRepository repository)
    {
        return repository.Container.TryReadItemWithETagAsync(
            CosmosConfigurationTemplateService.GetTemplateId(key.ViewName, typeCode),
            PartitionKeys.GetCosmosTemplate(key.ProjectName),
            stream => stream.DeserializeNewtonsoft<GenerateTemplateEntity>(_serializerOptions));
    }

    private async Task<bool> HaveTemplatesChangedAsync(FullKey key, IContainerRepository repository, Dictionary<string, TemplateSnapshot> templates)
    {
        foreach (var (typeCode, template) in templates)
        {
            var (_, etag) = await ReadTemplateAsync(key, typeCode, repository);
            if (etag != template.ETag)
            {
                return true;
            }
        }

        return false;
    }

    private async Task CollectHierarchyNodesAsync(
        InheritanceGraphNode node,
        IContainerRepository repository,
        JObject result,
        HashSet<string> visited)
    {
        var annotationKeyString = node.Key.Annotation.ToString();
        if (!visited.Add(annotationKeyString))
        {
            return;
        }

        foreach (var ancestorNode in node.GetAncestors())
        {
            await CollectHierarchyNodesAsync(ancestorNode, repository, result, visited);
        }

        var key = node.Key;
        var id = $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{key.Annotation}";
        var partitionKey = key.GetCosmosPartitionKey();
        var configuration = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        if (configuration is not null)
        {
            result[annotationKeyString] = configuration.Content;
        }
    }

    private async Task<JObject?> CalculateAndCacheConfigurationAsync(
        InheritanceGraphNode node,
        IContainerRepository repository,
        Dictionary<string, TemplateSnapshot> templates)
    {
        var key = node.Key;
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);
        var annotationKeyString = key.Annotation.ToString();
        var configEntryId = $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{annotationKeyString}";
        var (configEntry, configEntryETag) = await repository.Container.TryReadItemWithETagAsync(
            configEntryId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));
        var exist = configEntry is not null;

        // If the configuration is already calculated, return it directly (cached in DB)
        if (exist && configEntry.CalculatedContent is not null)
        {
            return configEntry.CalculatedContent;
        }

        var config = new JObject();

        // Inherit parent configurations (responsibility <|- unit <|- subject <|- usage <|- context <|- execution <|- unit-of-execution) as graph
        foreach (var ancestorNode in node.GetAncestors())
        {
            var parentConfig = await CalculateAndCacheConfigurationAsync(ancestorNode, repository, templates);
            if (parentConfig is not null)
            {
                config.MergeInto(parentConfig);
            }
        }

        // Fill auto-generated content for the type from the view template (if any)
        await TryAutogeneratePropertiesAsync(config, key, repository, templates);

        // The most specific configuration has precedence, is merged last
        if (exist)
        {
            config.MergeInto(configEntry!.Content);

            if (config.HasValues)
            {
                var hash = config.CalculateMurmurHash32Bits(_jsonSettingsProvider.GetSettings(JsonSettingNames.Unique));
                configEntry = configEntry with
                {
                    CalculatedContent = config,
                    CalculatedContentHash = $"{hash:x8}",
                };

                // an invalidation (or any other write) since the read means this calculation can be stale, then it is not cached
                try
                {
                    await repository.Container.ReplaceItemAsync(
                        configEntry,
                        configEntryId,
                        partitionKey,
                        new ItemRequestOptions { IfMatchEtag = configEntryETag, EnableContentResponseOnWrite = false });
                }
                catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
                {
                    // the next read calculates again
                }
            }
        }
        else if (config.HasValues)
        {
            var hash = config.CalculateMurmurHash32Bits(_jsonSettingsProvider.GetSettings(JsonSettingNames.Unique));
            ItemResponse<ConfigurationEntity> created;

            try
            {
                created = await repository.Container.CreateItemAsync(
                    new ConfigurationEntity
                    {
                        Id = configEntryId,
                        PartitionKey = partitionKeyValue,
                        AnnotationKey = annotationKeyString,
                        IsServerSubstitutionDisabled = false,
                        Name = key.Annotation.Name,
                        ProjectName = key.ProjectName,
                        ViewName = key.ViewName,
                        Content = new JObject(),
                        CalculatedContent = config,
                        CalculatedContentHash = $"{hash:x8}",
                        Modified = AuditStamp.Of(AuditActor.System, _timeProvider.GetUtcNow()),
                    },
                    partitionKey,
                    new ItemRequestOptions { EnableContentResponseOnWrite = false });
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // created meanwhile by another calculation or write, keep theirs
                return config;
            }

            // an invalidation cannot reach an item which didn't exist yet, verify the used templates are still the same
            if (await HaveTemplatesChangedAsync(key, repository, templates))
            {
                using var clearResponse = await repository.Container.PatchItemStreamAsync(
                    configEntryId,
                    partitionKey,
                    [
                        PatchOperation.Set<object?>($"/{nameof(ConfigurationEntity.CalculatedContent)}", null),
                        PatchOperation.Set<object?>($"/{nameof(ConfigurationEntity.CalculatedContentHash)}", null),
                    ],
                    new PatchItemRequestOptions { IfMatchEtag = created.ETag });
            }
        }

        return config;
    }

    private async Task InvalidateConfigurationsAsync(FullKey key, IContainerRepository repository)
    {
        switch (key.Annotation.Type)
        {
            case AnnotationType.Responsibility:
            {
                // invalidate all units, usages, executions, and unit-of-executions (one responsibility partition)
                // invalidate all configuration in one responsibility partition
                await ConfigurationCacheInvalidator.InvalidateAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}."),
                    key.GetCosmosPartitionKey());
                break;
            }

            case AnnotationType.Unit:
            {
                // invalidate all unit-of-executions (one responsibility partition)
                await ConfigurationCacheInvalidator.InvalidateAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && (config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.UnitOfExecution}.") && config.Id.EndsWith($".{key.Annotation.UnitName}")),
                    key.GetCosmosPartitionKey());
                return;
            }

            case AnnotationType.Usage:
            {
                // invalidate all executions, unit-of-executions (one responsibility partition)
                await ConfigurationCacheInvalidator.InvalidateAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && (config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Execution}.{key.Annotation.SubjectName}.")
                            || config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.UnitOfExecution}.{key.Annotation.SubjectName}.")),
                    key.GetCosmosPartitionKey());
                break;
            }

            case AnnotationType.Subject:
            {
                // invalidate all usages, contexts, executions, unit-of-executions
                // invalidate the context configurations in the subject partitions
                await ConfigurationCacheInvalidator.InvalidateAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Context}."),
                    key.GetCosmosPartitionKey());

                // invalidate usages, executions, unit-of-executions in the responsibility partitions
                await ConfigurationCacheInvalidator.InvalidateAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && (config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Usage}.{key.Annotation.SubjectName}.")
                            || config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Execution}.{key.Annotation.SubjectName}.")
                            || config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.UnitOfExecution}.{key.Annotation.SubjectName}.")));
                break;
            }

            case AnnotationType.Context:
            {
                // invalidate executions, and unit-of-executions configurations in the responsibility partitions
                await ConfigurationCacheInvalidator.InvalidateAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && ((config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Execution}.{key.Annotation.SubjectName}.") && config.Id.EndsWith($".{key.Annotation.ContextName}"))
                            || (config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.UnitOfExecution}.{key.Annotation.SubjectName}.") && config.Id.Contains($".{key.Annotation.ContextName}."))));
                break;
            }

            case AnnotationType.Execution:
            {
                // invalidate unit-of-executions (one responsibility partition)
                await ConfigurationCacheInvalidator.InvalidateAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.UnitOfExecution}.{key.Annotation.SubjectName}.{key.Annotation.ResponsibilityName}.{key.Annotation.ContextName}."),
                    key.GetCosmosPartitionKey());
                return;
            }

            case AnnotationType.UnitOfExecution:
            {
                // nothing to invalidate (it is the deepest configuration)
                return;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(key.Annotation.Type));
            }
        }
    }

    private async Task ForceDeleteConfigurationAsync(FullKey key, IContainerRepository repository)
    {
        var id = $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{key.Annotation}";
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);
        var existingConfiguration = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        if (existingConfiguration is null)
        {
            // if there is no entity
            return;
        }

        if (!existingConfiguration.Content.HasValues)
        {
            // if there is only precalculated entity with no content it can be directly deleted
            await repository.Container.DeleteItemStreamAsync(
                id,
                partitionKey,
                new ItemRequestOptions { EnableContentResponseOnWrite = false });
            return;
        }

        var transaction = repository.Container.CreateTransactionalBatch(partitionKey);
        var historyVersion = existingConfiguration.Version;
        var annotationKey = key.Annotation.ToString();
        var historyKey = $"{EntityIdPrefixTypes.ConfigurationVersion}.{annotationKey}.{historyVersion}";
        var historyId = $"{key.ViewName}.{historyKey}";
        var history = new ConfigurationHistoryEntity
        {
            PartitionKey = partitionKeyValue,
            Id = historyId,
            AnnotationKey = annotationKey,
            Version = historyVersion,
            Name = existingConfiguration.Name,
            ProjectName = existingConfiguration.ProjectName,
            ViewName = existingConfiguration.ViewName,
            Content = existingConfiguration.Content,
            Modified = existingConfiguration.Modified,
        };
        transaction.CreateItem(history);
        transaction.DeleteItem(id);
        await transaction.ExecuteAsync();
    }

    // The highest version in the history of the configuration, 0 without one.
    private static async Task<ulong> GetHighestArchivedVersionAsync(FullKey key, IContainerRepository repository)
    {
        var maxVersionResponse = await repository.Container.GetItemLinqQueryable<ConfigurationHistoryEntity>(
                requestOptions: new QueryRequestOptions { PartitionKey = key.GetCosmosPartitionKey() })
            .Where(history => history.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.ConfigurationVersion}.{key.Annotation}."))
            .Select(history => history.Version)
            .MaxAsync();

        return maxVersionResponse.Resource;
    }

    // Keys of the soft-deleted annotations of a view, whose configuration versions are hidden.
    private static async Task<HashSet<string>> ReadDeletedAnnotationKeysAsync(Container container, string project, string view, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT VALUE c.AnnotationKey FROM c WHERE STARTSWITH(c.PartitionKey, @projectPrefix) AND c.ProjectName = @project "
                + "AND c.ViewName = @view AND IS_DEFINED(c.AnnotationType) AND IS_DEFINED(c.Deletion)")
            .WithParameter("@projectPrefix", $"{project}.")
            .WithParameter("@project", project)
            .WithParameter("@view", view);

        var keys = new HashSet<string>(StringComparer.Ordinal);
        using var iterator = container.GetItemQueryIterator<string>(query);

        while (iterator.HasMoreResults)
        {
            foreach (var key in await iterator.ReadNextAsync(cancellationToken))
            {
                if (!string.IsNullOrEmpty(key))
                {
                    keys.Add(key);
                }
            }
        }

        return keys;
    }

    private static ViewImportResult DeletedImport(string annotationKey)
    {
        return new ViewImportResult(
            ViewImportOutcome.Skipped,
            ErrorCode: ErrorCodes.AnnotationDeleted,
            Message: $"The annotation {annotationKey} is deleted; restore it before importing its configuration.");
    }

    // A soft-deleted annotation's configuration cannot change until a restore.
    private static async Task EnsureAnnotationNotDeletedAsync(FullKey key, IContainerRepository repository, ConfigurationEntity? existingConfiguration)
    {
        var isDeleted = existingConfiguration is not null
            ? existingConfiguration.AnnotationDeletionId is not null
            : await AnnotationItemStore.ReadStateAsync(repository.Container, key) == AnnotationItemState.Deleted;

        if (isDeleted)
        {
            throw new ConflictException(
                $"The annotation '{key.Annotation}' is deleted; restore it before changing its configuration.",
                ErrorCodes.AnnotationDeleted);
        }
    }

    // Configuration changes appear in the annotation's history as version references. Best effort: the configuration
    // is already written, so a failed append is logged and the request still succeeds.
    private async Task AuditConfigurationAsync(FullKey key, IContainerRepository repository, AuditActor? actor, AnnotationAuditAction action, ulong version)
    {
        if (actor is null || _auditStore is null)
        {
            return;
        }

        try
        {
            var entry = AnnotationAuditStore.NewEntry(action, actor, _auditStore.GetNow()) with { ConfigurationVersion = version };
            await _auditStore.AppendAsync(repository.Container, key, entry);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The configuration change of {AnnotationKey} was not recorded in its audit.", key.Annotation.ToString());
        }
    }

    private static InheritanceGraphNode BuildInheritanceGraph(FullKey key)
    {
        var targetNode = new InheritanceGraphNode(key);
        var annotationKey = key.Annotation;

        switch (annotationKey.Type)
        {
            case AnnotationType.Responsibility:
            case AnnotationType.Subject:
                // top notes
                break;

            case AnnotationType.Unit:
            {
                targetNode.CreateAncestor(FullKey.Create(annotationKey.GetResponsibilityKey(), key));
                break;
            }

            case AnnotationType.Usage:
            {
                targetNode.CreateAncestor(FullKey.Create(annotationKey.GetResponsibilityKey(), key));
                targetNode.CreateAncestor(FullKey.Create(annotationKey.GetSubjectKey(), key));
                break;
            }

            case AnnotationType.Context:
            {
                targetNode.CreateAncestor(FullKey.Create(annotationKey.GetSubjectKey(), key));
                break;
            }

            case AnnotationType.Execution:
            {
                var usageNode = targetNode.CreateAncestor(FullKey.Create(annotationKey.GetUsageKey(), key));
                usageNode.CreateAncestor(FullKey.Create(annotationKey.GetResponsibilityKey(), key));
                var subjectNode = usageNode.CreateAncestor(FullKey.Create(annotationKey.GetSubjectKey(), key));
                var contextNode = targetNode.CreateAncestor(FullKey.Create(annotationKey.GetContextKey(), key));
                contextNode.AddAncestor(subjectNode);
                break;
            }

            case AnnotationType.UnitOfExecution:
            {
                var executionNode = targetNode.CreateAncestor(FullKey.Create(annotationKey.GetExecutionKey(), key));
                var unitNode = targetNode.CreateAncestor(FullKey.Create(annotationKey.GetUnitKey(), key));
                var usageNode = executionNode.CreateAncestor(FullKey.Create(annotationKey.GetUsageKey(), key));
                var responsibilityNode = usageNode.CreateAncestor(FullKey.Create(annotationKey.GetResponsibilityKey(), key));
                var subjectNode = usageNode.CreateAncestor(FullKey.Create(annotationKey.GetSubjectKey(), key));
                var contextNode = executionNode.CreateAncestor(FullKey.Create(annotationKey.GetContextKey(), key));
                contextNode.AddAncestor(subjectNode);
                unitNode.AddAncestor(responsibilityNode);
                break;
            }

            default:
                throw new InvalidOperationException("Configuration of an unknown annotation type.");
        }

        return targetNode;
    }

    private sealed record class TemplateSnapshot(JObject? Content, string? ETag);
}
