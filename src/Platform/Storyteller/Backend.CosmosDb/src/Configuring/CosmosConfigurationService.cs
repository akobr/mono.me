using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Text;
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
    private const int MaxRetries = 3;

    // below the 2 MB request limit of a transactional batch, as in BatchPlan
    private const long MaxBatchPayloadBytes = 1_500_000;

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
        var calculatedConfig = await CalculateAndCacheConfigurationAsync(node, repository);

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

    public async Task<Configuration> ResolveConfigurationAsync(FullKey key, Configuration effective, bool includeSecrets = false, BindingDependencies? dependencies = null)
    {
        ArgumentNullException.ThrowIfNull(effective);

        if (_bindingResolver is null)
        {
            return effective;
        }

        if (_bindingResolver is not IRecordingBinding)
        {
            dependencies?.MarkVolatile("The binding resolver does not record its reads.");
        }

        var scope = new BindingScope
        {
            Document = effective.Content.DeepClone(),
            Context = new ConfigurationBindingContext(key),
            Dependencies = dependencies,
        };

        await _bindingResolver.ResolveAsync(effective.Content, includeSecrets, scope);
        return effective;
    }

    public Task<bool> AreDependenciesUnchangedAsync(FullKey key, IReadOnlyList<BindingDependency> dependencies)
    {
        return new BindingDependencyValidator(_repositoryProvider, _logger).AreUnchangedAsync(key, dependencies);
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

        var history = new List<ConfigurationHistoryEntity>();
        while (feed.HasMoreResults)
        {
            history.AddRange(await feed.ReadNextAsync());
        }

        var configurationKey = $"{EntityIdPrefixTypes.Configuration}.{key.Annotation}";
        var configurationId = $"{key.ViewName}.{configurationKey}";

        var configuration = await repository.Container.TryReadItemAsync(
            configurationId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        // an empty archive of version 0 holds no written version (an old patch archived cache rows so), and an archive
        // of the current or a later version is a leftover that a write counts as archived, as in an export
        var versions = history
            .Where(entity => entity.Version > 0 || entity.Content.HasValues)
            .Where(entity => configuration is null || configuration.Version == 0 || entity.Version < configuration.Version)
            .Select(entity => entity.ToConfigurationVersion())
            .ToList();

        // a cache row (version 0, no content) only caches the effective document, it is no version
        if (configuration is not null && IsWrittenVersion(configuration))
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
        var configId = $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{annotationKey}";
        var configEntity = await repository.Container.TryReadItemAsync(
            configId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        // the current written version wins over an archive of the same version, which can be a leftover of another
        // lifetime; a cache row (version 0) does not hide an archive of version 0 with content
        if (configEntity is not null
            && IsWrittenVersion(configEntity)
            && configEntity.Version == version)
        {
            return configEntity.ToConfigurationFromContent();
        }

        var configVersionId = $"{key.ViewName}.{EntityIdPrefixTypes.ConfigurationVersion}.{annotationKey}.{version}";
        var versionEntity = await repository.Container.TryReadItemAsync(
            configVersionId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationHistoryEntity>(_serializerOptions));

        // a cache row is no stored version, so it gives no content
        return versionEntity?.ToConfiguration();
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

    // Merges the value into the stored document, or replaces the document with it (an import). Every attempt reads the
    // row with its ETag and writes guarded by it, so a concurrent change starts the attempt over.
    private async Task<Configuration> WriteConfigurationAsync(FullKey key, JObject value, AuditActor actor, bool force, bool merge)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var annotationKey = key.Annotation.ToString();
        var id = GetConfigurationId(key);
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existingConfiguration, etag) = await ReadForWriteAsync(key, repository, id, partitionKey);

            // the requested operations change their input, each attempt works on a fresh copy
            var input = (JObject)value.DeepClone();

            if (existingConfiguration is null)
            {
                // create a new configuration if there is nothing yet
                input = input.RemoveRequested();
                input = await input.ApplyPatchRequested();

                if (!force && _schemaService is not null && input.HasValues)
                {
                    await _schemaService.ValidateContentAsync(key.OrganizationName, key.ProjectName, key.ViewName, annotationKey, input);
                }

                var configuration = new ConfigurationEntity
                {
                    PartitionKey = partitionKeyValue,
                    Id = id,
                    AnnotationKey = annotationKey,
                    IsServerSubstitutionDisabled = false,
                    Name = key.Annotation.Name,
                    ProjectName = key.ProjectName,
                    ViewName = key.ViewName,
                    Content = input,
                    Version = await GetHighestArchivedVersionAsync(key, repository) + 1,
                    Modified = AuditStamp.Of(actor, _timeProvider.GetUtcNow()),
                    PendingInvalidationId = NewInvalidationId(key),
                };

                // a calculation may have created the row meanwhile (409), then the next attempt updates it
                var batch = repository.Container.CreateTransactionalBatch(partitionKey);
                batch.CreateItem(configuration);

                if (!await TryExecuteWriteAsync(batch, annotationKey, partitionKeyValue))
                {
                    continue;
                }

                // invalidate the cached calculations of all descendants once the document exists, so a calculation
                // that read it as missing is either cleared here or fails to cache on the changed ETag
                await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationChanged, configuration.Version);
                await CompleteInvalidationAsync(key, repository, configuration.PendingInvalidationId);
                return configuration.ToConfiguration();
            }

            JObject newContent;

            // merge into stored content when there is some; an empty document is replaced
            if (merge && existingConfiguration.Content.HasValues)
            {
                newContent = (JObject)existingConfiguration.Content.DeepClone();
                newContent.MergeInto(input);
                newContent = newContent.RemoveRequested();
                newContent = await newContent.ApplyPatchRequested();
            }
            else
            {
                newContent = input.RemoveRequested();
                newContent = await newContent.ApplyPatchRequested();
            }

            // an equal document writes nothing, also an empty one over an emptied version; a cache row is no version
            // yet, so any document written over it is its first one
            if (IsWrittenVersion(existingConfiguration)
                && JToken.DeepEquals(existingConfiguration.Content, newContent))
            {
                return existingConfiguration.ToConfigurationFromContent();
            }

            // validate before any write, so a rejection cannot leave a history item
            if (!force && _schemaService is not null && newContent.HasValues)
            {
                await _schemaService.ValidateContentAsync(key.OrganizationName, key.ProjectName, key.ViewName, annotationKey, newContent);
            }

            var newConfiguration = await TryWriteNextVersionAsync(key, repository, existingConfiguration, etag!, newContent, actor);

            if (newConfiguration is null)
            {
                continue;
            }

            await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationChanged, newConfiguration.Version);
            await CompleteInvalidationAsync(key, repository, newConfiguration.PendingInvalidationId);
            return newConfiguration.ToConfigurationFromContent();
        }

        throw ConcurrentChange(annotationKey);
    }

    public async Task<Configuration> PatchConfigurationAsync(FullKey key, JArray patchOperations, AuditActor actor, bool force = false)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var annotationKey = key.Annotation.ToString();
        var id = GetConfigurationId(key);
        var partitionKey = key.GetCosmosPartitionKey();

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existingConfiguration, etag) = await ReadForWriteAsync(key, repository, id, partitionKey);

            if (existingConfiguration is null)
            {
                throw new ConfigurationNotFoundException(key.Annotation);
            }

            // the patch is applied to the content of this attempt's read, the operations stay unchanged
            var newContent = await existingConfiguration.Content.ApplyPatch(patchOperations);

            if (JToken.DeepEquals(existingConfiguration.Content, newContent))
            {
                return existingConfiguration.ToConfigurationFromContent();
            }

            if (!force && _schemaService is not null && newContent.HasValues)
            {
                await _schemaService.ValidateContentAsync(key.OrganizationName, key.ProjectName, key.ViewName, annotationKey, newContent);
            }

            var newConfiguration = await TryWriteNextVersionAsync(key, repository, existingConfiguration, etag!, newContent, actor);

            if (newConfiguration is null)
            {
                continue;
            }

            await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationChanged, newConfiguration.Version);
            await CompleteInvalidationAsync(key, repository, newConfiguration.PendingInvalidationId);
            return newConfiguration.ToConfigurationFromContent();
        }

        throw ConcurrentChange(annotationKey);
    }

    public async Task<bool> ClearConfigurationAsync(FullKey key, AuditActor? actor = null)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var annotationKey = key.Annotation.ToString();
        var id = GetConfigurationId(key);
        var partitionKey = key.GetCosmosPartitionKey();

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            // finishes also the invalidation of an earlier clear, which a repeated clear finds without a document
            var (existingConfiguration, etag) = await ReadForWriteAsync(key, repository, id, partitionKey);

            if (existingConfiguration is null
                || !existingConfiguration.Content.HasValues)
            {
                // if there is no entity or there is no configuration content (only pre-calculated content)
                return false;
            }

            var archived = await TryArchiveAndDeleteAsync(key, repository, existingConfiguration, etag!);

            if (archived is null)
            {
                continue;
            }

            await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationDeleted, archived.Version);
            await CompletePendingClearsAsync(key, repository, archived);
            return true;
        }

        throw ConcurrentChange(annotationKey);
    }

    public async Task DeleteAsync(FullKey key, AuditActor? actor = null)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var (deletedConfiguration, archived) = await ForceDeleteConfigurationAsync(key, repository);

        if (deletedConfiguration?.Content.HasValues == true)
        {
            await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationDeleted, archived?.Version ?? deletedConfiguration.Version);
        }

        // always invalidates, so a repeated delete finishes the invalidation of an earlier one
        await CompletePendingClearsAsync(key, repository, archived, always: true);
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
            if (!options.DryRun)
            {
                // finish an invalidation of an earlier committed change, for example of an interrupted import
                await CompletePendingWorkAsync(key, repository, existing);
            }

            return new ViewImportResult(ViewImportOutcome.Unchanged, existing.Version);
        }

        if (hasContent && options.Existing == ViewImportExisting.Skip)
        {
            if (!options.DryRun)
            {
                await CompletePendingWorkAsync(key, repository, existing);
            }

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

    // Merges the view template of the node's type. It is read on every calculation, after the node's item, so a cache
    // written under the ETag of that read never holds a template older than the read.
    private async Task TryAutogeneratePropertiesAsync(JObject config, FullKey key, IContainerRepository repository)
    {
        var (template, _) = await ReadTemplateAsync(key, key.Annotation.TypeCode.ToLowerInvariant(), repository);

        if (template?.Content is null)
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

    // Calculates the effective document of the node and caches it on its item. A cache is written only under an ETag
    // the item had before every read of the calculation: the item is read first, then the ancestors and the template.
    // An invalidation that patches the item meanwhile makes the cache write fail, and a missing item is created first,
    // so that it is either reached by an invalidation or created after it.
    private async Task<JObject?> CalculateAndCacheConfigurationAsync(InheritanceGraphNode node, IContainerRepository repository)
    {
        var key = node.Key;
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);
        var configEntryId = GetConfigurationId(key);
        var (configEntry, configEntryETag) = await ReadConfigurationAsync(repository, configEntryId, partitionKey);

        // If the configuration is already calculated, return it directly (cached in DB)
        if (configEntry?.CalculatedContent is not null)
        {
            return configEntry.CalculatedContent;
        }

        var config = await CalculateConfigurationAsync(node, repository, configEntry);

        if (!config.HasValues)
        {
            return config;
        }

        if (configEntry is not null)
        {
            await TryCacheConfigurationAsync(repository, configEntry, configEntryETag!, config);
            return config;
        }

        // an invalidation cannot reach an item that does not exist yet, so the item is created without a cache first,
        // and the calculation runs again from reads after the creation, under its ETag
        var createdEntry = new ConfigurationEntity
        {
            Id = configEntryId,
            PartitionKey = partitionKeyValue,
            AnnotationKey = key.Annotation.ToString(),
            IsServerSubstitutionDisabled = false,
            Name = key.Annotation.Name,
            ProjectName = key.ProjectName,
            ViewName = key.ViewName,
            Content = new JObject(),
            Modified = AuditStamp.Of(AuditActor.System, _timeProvider.GetUtcNow()),
        };

        ItemResponse<ConfigurationEntity> created;

        try
        {
            created = await repository.Container.CreateItemAsync(
                createdEntry,
                partitionKey,
                new ItemRequestOptions { EnableContentResponseOnWrite = false });
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            // created meanwhile by another calculation or write, keep theirs
            return config;
        }

        config = await CalculateConfigurationAsync(node, repository, createdEntry);

        if (config.HasValues)
        {
            await TryCacheConfigurationAsync(repository, createdEntry, created.ETag, config);
        }

        return config;
    }

    // The effective document of the node: the documents of its ancestors, the template of its type, and its own
    // content, merged in this order.
    private async Task<JObject> CalculateConfigurationAsync(InheritanceGraphNode node, IContainerRepository repository, ConfigurationEntity? configEntry)
    {
        var config = new JObject();

        // Inherit parent configurations (responsibility <|- unit <|- subject <|- usage <|- context <|- execution <|- unit-of-execution) as graph
        foreach (var ancestorNode in node.GetAncestors())
        {
            var parentConfig = await CalculateAndCacheConfigurationAsync(ancestorNode, repository);
            if (parentConfig is not null)
            {
                config.MergeInto(parentConfig);
            }
        }

        // Fill auto-generated content for the type from the view template (if any)
        await TryAutogeneratePropertiesAsync(config, node.Key, repository);

        // The most specific configuration has precedence, is merged last
        if (configEntry is not null)
        {
            config.MergeInto(configEntry.Content);
        }

        return config;
    }

    // An invalidation (or any other write) since the ETag was taken means the calculation can be stale; then it is
    // not cached, and the next read calculates again.
    private async Task TryCacheConfigurationAsync(IContainerRepository repository, ConfigurationEntity configEntry, string etag, JObject config)
    {
        var hash = config.CalculateMurmurHash32Bits(_jsonSettingsProvider.GetSettings(JsonSettingNames.Unique));

        try
        {
            await repository.Container.ReplaceItemAsync(
                configEntry with
                {
                    CalculatedContent = config,
                    CalculatedContentHash = $"{hash:x8}",
                },
                configEntry.Id,
                new PartitionKey(configEntry.PartitionKey),
                new ItemRequestOptions { IfMatchEtag = etag, EnableContentResponseOnWrite = false });
        }
        catch (CosmosException ex) when (ex.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            // the next read calculates again
        }
    }

    private Task InvalidateConfigurationsAsync(FullKey key, IContainerRepository repository)
    {
        var targets = GetDescendantTargets(key);
        return targets.Count == 0
            ? Task.CompletedTask
            : ConfigurationCacheInvalidator.InvalidateAsync(repository, targets);
    }

    // The descendants whose effective document includes the annotation, by type (see BuildInheritanceGraph); the
    // invalidator patches them level by level. The written item is no target: its own write has cleared its cache.
    private static IReadOnlyCollection<InvalidationTarget> GetDescendantTargets(FullKey key)
    {
        var project = key.ProjectName;
        var view = key.ViewName;
        var prefix = $"{view}.{EntityIdPrefixTypes.Configuration}.";
        var annotation = key.Annotation;
        var partitionKey = key.GetCosmosPartitionKey();

        InvalidationTarget Target(string typeCode, string idPrefix, PartitionKey? targetPartitionKey)
        {
            return new InvalidationTarget(
                typeCode,
                config => config.ProjectName == project && config.ViewName == view && config.Id.StartsWith(idPrefix),
                targetPartitionKey);
        }

        switch (annotation.Type)
        {
            case AnnotationType.Responsibility:
            {
                // all units, usages, executions and units of execution (one responsibility partition)
                return
                [
                    Target(AnnotationTypeCodes.Unit, $"{prefix}{AnnotationTypeCodes.Unit}.", partitionKey),
                    Target(AnnotationTypeCodes.Usage, $"{prefix}{AnnotationTypeCodes.Usage}.", partitionKey),
                    Target(AnnotationTypeCodes.Execution, $"{prefix}{AnnotationTypeCodes.Execution}.", partitionKey),
                    Target(AnnotationTypeCodes.UnitOfExecution, $"{prefix}{AnnotationTypeCodes.UnitOfExecution}.", partitionKey),
                ];
            }

            case AnnotationType.Unit:
            {
                // all units of execution of the unit (one responsibility partition)
                var idPrefix = $"{prefix}{AnnotationTypeCodes.UnitOfExecution}.";
                var idSuffix = $".{annotation.UnitName}";
                return
                [
                    new InvalidationTarget(
                        AnnotationTypeCodes.UnitOfExecution,
                        config => config.ProjectName == project && config.ViewName == view && config.Id.StartsWith(idPrefix) && config.Id.EndsWith(idSuffix),
                        partitionKey),
                ];
            }

            case AnnotationType.Usage:
            {
                // all executions and units of execution of the subject (one responsibility partition)
                return
                [
                    Target(AnnotationTypeCodes.Execution, $"{prefix}{AnnotationTypeCodes.Execution}.{annotation.SubjectName}.", partitionKey),
                    Target(AnnotationTypeCodes.UnitOfExecution, $"{prefix}{AnnotationTypeCodes.UnitOfExecution}.{annotation.SubjectName}.", partitionKey),
                ];
            }

            case AnnotationType.Subject:
            {
                // the contexts in the subject partition, the usages, executions and units of execution in the
                // responsibility partitions
                return
                [
                    Target(AnnotationTypeCodes.Context, $"{prefix}{AnnotationTypeCodes.Context}.", partitionKey),
                    Target(AnnotationTypeCodes.Usage, $"{prefix}{AnnotationTypeCodes.Usage}.{annotation.SubjectName}.", null),
                    Target(AnnotationTypeCodes.Execution, $"{prefix}{AnnotationTypeCodes.Execution}.{annotation.SubjectName}.", null),
                    Target(AnnotationTypeCodes.UnitOfExecution, $"{prefix}{AnnotationTypeCodes.UnitOfExecution}.{annotation.SubjectName}.", null),
                ];
            }

            case AnnotationType.Context:
            {
                // the executions and units of execution of the context in the responsibility partitions
                var executionPrefix = $"{prefix}{AnnotationTypeCodes.Execution}.{annotation.SubjectName}.";
                var executionSuffix = $".{annotation.ContextName}";
                var unitOfExecutionPrefix = $"{prefix}{AnnotationTypeCodes.UnitOfExecution}.{annotation.SubjectName}.";
                var unitOfExecutionPart = $".{annotation.ContextName}.";
                return
                [
                    new InvalidationTarget(
                        AnnotationTypeCodes.Execution,
                        config => config.ProjectName == project && config.ViewName == view && config.Id.StartsWith(executionPrefix) && config.Id.EndsWith(executionSuffix)),
                    new InvalidationTarget(
                        AnnotationTypeCodes.UnitOfExecution,
                        config => config.ProjectName == project && config.ViewName == view && config.Id.StartsWith(unitOfExecutionPrefix) && config.Id.Contains(unitOfExecutionPart)),
                ];
            }

            case AnnotationType.Execution:
            {
                // the units of execution of the execution (one responsibility partition)
                return
                [
                    Target(
                        AnnotationTypeCodes.UnitOfExecution,
                        $"{prefix}{AnnotationTypeCodes.UnitOfExecution}.{annotation.SubjectName}.{annotation.ResponsibilityName}.{annotation.ContextName}.",
                        partitionKey),
                ];
            }

            case AnnotationType.UnitOfExecution:
            {
                // nothing to invalidate (it is the deepest configuration)
                return [];
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(key.Annotation.Type));
            }
        }
    }

    // Deletes the row: a written version is archived first, a cache row (version 0) is deleted directly. Returns the
    // deleted row, and the archive with its pending invalidation.
    private async Task<(ConfigurationEntity? Deleted, PendingClear? Archived)> ForceDeleteConfigurationAsync(FullKey key, IContainerRepository repository)
    {
        var annotationKey = key.Annotation.ToString();
        var id = GetConfigurationId(key);
        var partitionKey = key.GetCosmosPartitionKey();

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existingConfiguration, etag) = await ReadConfigurationAsync(repository, id, partitionKey);
            await EnsureAnnotationNotDeletedAsync(key, repository, existingConfiguration);

            if (existingConfiguration is null)
            {
                // if there is no entity
                return (null, null);
            }

            if (!IsWrittenVersion(existingConfiguration))
            {
                // a cache row is no version, it is deleted directly
                using var response = await repository.Container.DeleteItemStreamAsync(
                    id,
                    partitionKey,
                    new ItemRequestOptions { IfMatchEtag = etag, EnableContentResponseOnWrite = false });

                if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    continue;
                }

                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
                {
                    throw new ConfigurationStorageException(
                        $"Failed to delete configuration '{annotationKey}' with status {response.StatusCode}.",
                        response.StatusCode);
                }

                return (existingConfiguration, null);
            }

            var archived = await TryArchiveAndDeleteAsync(key, repository, existingConfiguration, etag!);

            if (archived is null)
            {
                continue;
            }

            return (existingConfiguration, archived);
        }

        throw ConcurrentChange(annotationKey);
    }

    private Task<(ConfigurationEntity? Item, string? ETag)> ReadConfigurationAsync(IContainerRepository repository, string id, PartitionKey partitionKey)
    {
        return repository.Container.TryReadItemWithETagAsync(
            id,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));
    }

    // Decides how the stored version is archived, see ArchivePlan.
    private async Task<ArchivePlan> PlanArchiveAsync(FullKey key, IContainerRepository repository, ConfigurationEntity existing)
    {
        if (!IsWrittenVersion(existing))
        {
            // a cache row is no version; the next one continues after the archived versions, which a clear left behind
            return new ArchivePlan(null, IsStored: false, await GetHighestArchivedVersionAsync(key, repository) + 1);
        }

        var stored = await ReadHistoryAsync(key, repository, existing.Version);

        if (stored is null || IsArchiveOf(stored, existing))
        {
            // an equal archive is left by a write that failed after its archive, or by a snapshot of the annotation
            // lifecycle; a version 0 with content was written before versions were counted, the next one follows the
            // archived ones
            var next = existing.Version > 0
                ? existing.Version + 1
                : await GetHighestArchivedVersionAsync(key, repository) + 1;
            return new ArchivePlan(existing.Version, IsStored: stored is not null, next);
        }

        // the version is held by an item of an earlier lifetime of the configuration, which is kept; the current
        // content goes after the highest archived version, unless an earlier attempt has put it there already
        var highest = await GetHighestArchivedVersionAsync(key, repository);
        var latest = highest == existing.Version ? stored : await ReadHistoryAsync(key, repository, highest);

        return latest is not null && IsArchiveOf(latest, existing)
            ? new ArchivePlan(highest, IsStored: true, highest + 1)
            : new ArchivePlan(highest + 1, IsStored: false, highest + 2);
    }

    private Task<ConfigurationHistoryEntity?> ReadHistoryAsync(FullKey key, IContainerRepository repository, ulong version)
    {
        return repository.Container.TryReadItemAsync(
            GetHistoryId(key, version),
            key.GetCosmosPartitionKey(),
            stream => stream.DeserializeNewtonsoft<ConfigurationHistoryEntity>(_serializerOptions));
    }

    private static bool IsArchiveOf(ConfigurationHistoryEntity history, ConfigurationEntity current)
    {
        return JToken.DeepEquals(history.Content, current.Content) && history.Modified == current.Modified;
    }

    // Every write gives version 1 or more; a calculation creates a cache row with version 0 and no content. A version
    // 0 with content comes from the time before versions were counted.
    private static bool IsWrittenVersion(ConfigurationEntity configuration)
    {
        return configuration.Version > 0 || configuration.Content.HasValues;
    }

    // Archives the stored version and writes the next one, guarded by the ETag of the read. Returns null when another
    // writer changed or deleted the row meanwhile.
    private async Task<ConfigurationEntity?> TryWriteNextVersionAsync(
        FullKey key,
        IContainerRepository repository,
        ConfigurationEntity existing,
        string etag,
        JObject newContent,
        AuditActor actor)
    {
        var annotationKey = key.Annotation.ToString();
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);
        var archive = await PlanArchiveAsync(key, repository, existing);
        var history = archive.Version is { } archivedVersion && !archive.IsStored
            ? ToHistory(key, existing, archivedVersion, pendingInvalidationId: null)
            : null;

        var next = existing with
        {
            Version = archive.NextVersion,
            Content = newContent,
            Modified = AuditStamp.Of(actor, _timeProvider.GetUtcNow()),
            CalculatedContent = null,
            CalculatedContentHash = null,
            PendingInvalidationId = NewInvalidationId(key),
        };

        if (history is null
            || EstimateBytes(history.Content) + EstimateBytes(newContent) <= MaxBatchPayloadBytes)
        {
            // save history of the previous version together with the next version atomically
            var batch = repository.Container.CreateTransactionalBatch(partitionKey);

            if (history is not null)
            {
                batch.CreateItem(history);
            }

            batch.ReplaceItem(existing.Id, next, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
            return await TryExecuteWriteAsync(batch, annotationKey, partitionKeyValue) ? next : null;
        }

        // too large for one batch: the archive goes first, a failed write leaves it equal to the row, which the next
        // attempt counts as archived
        var isArchived = await TryWriteItemAsync(
            () => repository.Container.CreateItemAsync(history, partitionKey, new ItemRequestOptions { EnableContentResponseOnWrite = false }),
            annotationKey,
            partitionKeyValue);

        return isArchived
               && await TryWriteItemAsync(
                   () => repository.Container.ReplaceItemAsync(next, existing.Id, partitionKey, new ItemRequestOptions { IfMatchEtag = etag, EnableContentResponseOnWrite = false }),
                   annotationKey,
                   partitionKeyValue)
            ? next
            : null;
    }

    // Archives the stored version with the token of a pending invalidation and deletes the row in one batch, guarded
    // by the ETag of the read. Returns the archive, or null when another writer changed or deleted the row meanwhile.
    private async Task<PendingClear?> TryArchiveAndDeleteAsync(FullKey key, IContainerRepository repository, ConfigurationEntity existing, string etag)
    {
        var annotationKey = key.Annotation.ToString();
        var partitionKeyValue = key.GetPartitionKey();
        var archive = await PlanArchiveAsync(key, repository, existing);
        var version = archive.Version ?? throw new InvalidOperationException("Only a written version is archived.");
        var archived = new PendingClear(GetHistoryId(key, version), NewInvalidationId(key), version);
        var batch = repository.Container.CreateTransactionalBatch(new PartitionKey(partitionKeyValue));

        if (!archive.IsStored)
        {
            batch.CreateItem(ToHistory(key, existing, version, archived.Token));
        }
        else if (archived.Token is not null)
        {
            batch.PatchItem(archived.Id, [PatchOperation.Set($"/{nameof(ConfigurationHistoryEntity.PendingInvalidationId)}", archived.Token)]);
        }

        batch.DeleteItem(existing.Id, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
        return await TryExecuteWriteAsync(batch, annotationKey, partitionKeyValue) ? archived : null;
    }

    // Executes a configuration write batch. Returns false on a concurrent modification: the version archived (409),
    // the row changed (412) or deleted (404) by another writer.
    private async Task<bool> TryExecuteWriteAsync(TransactionalBatch batch, string annotationKey, string partitionKeyValue)
    {
        using var response = await batch.ExecuteAsync();

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            // the database details stay in the log, the caller gets only the status
            _logger.LogError(
                "Writing the configuration {AnnotationKey} in {PartitionKey} failed with {StatusCode} (activity {ActivityId}): {ErrorMessage}",
                annotationKey,
                partitionKeyValue,
                response.StatusCode,
                response.ActivityId,
                response.ErrorMessage);
            throw new ConfigurationStorageException(
                $"Failed to write configuration '{annotationKey}' with status {response.StatusCode}.",
                response.StatusCode);
        }

        return true;
    }

    // A single write outside a batch, classified like TryExecuteWriteAsync.
    private async Task<bool> TryWriteItemAsync(Func<Task> write, string annotationKey, string partitionKeyValue)
    {
        try
        {
            await write();
            return true;
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (CosmosException exception)
        {
            _logger.LogError(
                exception,
                "Writing the configuration {AnnotationKey} in {PartitionKey} failed with {StatusCode} (activity {ActivityId})",
                annotationKey,
                partitionKeyValue,
                exception.StatusCode,
                exception.ActivityId);
            throw new ConfigurationStorageException(
                $"Failed to write configuration '{annotationKey}' with status {exception.StatusCode}.",
                exception.StatusCode);
        }
    }

    // Finishes what an earlier request left pending on the configuration, before a write looks at its input: the
    // invalidation of the row's last write, and of the clears that archived the versions of a missing or empty row.
    // True when it did something, which can have changed the row since it was read.
    private async Task<bool> CompletePendingWorkAsync(FullKey key, IContainerRepository repository, ConfigurationEntity? existing)
    {
        var clears = existing is null || !existing.Content.HasValues
            ? await ReadPendingClearsAsync(key, repository)
            : [];

        if (existing?.PendingInvalidationId is null && clears.Count == 0)
        {
            return false;
        }

        await InvalidateAndCompleteAsync(key, repository, existing?.PendingInvalidationId, clears);
        return true;
    }

    // Reads the row for a write, after finishing what an earlier request left pending on it; finishing removes a token
    // from the row, so the row is read again for the ETag that guards the write.
    private async Task<(ConfigurationEntity? Item, string? ETag)> ReadForWriteAsync(FullKey key, IContainerRepository repository, string id, PartitionKey partitionKey)
    {
        var (existingConfiguration, etag) = await ReadConfigurationAsync(repository, id, partitionKey);
        await EnsureAnnotationNotDeletedAsync(key, repository, existingConfiguration);

        if (!await CompletePendingWorkAsync(key, repository, existingConfiguration))
        {
            return (existingConfiguration, etag);
        }

        (existingConfiguration, etag) = await ReadConfigurationAsync(repository, id, partitionKey);
        await EnsureAnnotationNotDeletedAsync(key, repository, existingConfiguration);
        return (existingConfiguration, etag);
    }

    // Invalidates the descendants of a write and removes its token from the row; a write without a token has none.
    private Task CompleteInvalidationAsync(FullKey key, IContainerRepository repository, string? token)
    {
        return token is null
            ? Task.CompletedTask
            : InvalidateAndCompleteAsync(key, repository, token, []);
    }

    // Invalidates the descendants of the clears that archived the versions of the configuration and removes their
    // tokens: of the given one and of every one still pending. Without any, only an unconditional call invalidates.
    private async Task CompletePendingClearsAsync(FullKey key, IContainerRepository repository, PendingClear? archived, bool always = false)
    {
        var clears = await ReadPendingClearsAsync(key, repository);

        if (archived?.Token is not null && clears.All(clear => clear.Id != archived.Id))
        {
            clears.Add(archived);
        }

        if (clears.Count == 0 && !always)
        {
            return;
        }

        await InvalidateAndCompleteAsync(key, repository, rowToken: null, clears);
    }

    // The tokens are known before the invalidation starts, so a write or clear that commits during it keeps its own.
    // A failed invalidation keeps every token, a later request finishes it.
    private async Task InvalidateAndCompleteAsync(FullKey key, IContainerRepository repository, string? rowToken, IReadOnlyCollection<PendingClear> clears)
    {
        try
        {
            await InvalidateConfigurationsAsync(key, repository);
        }
        catch (Exception exception)
        {
            // until the next write request on the configuration finishes it, descendants keep their cached documents
            _logger.LogWarning(
                exception,
                "The cache invalidation of the configuration {AnnotationKey} in {Organization}/{Project}/{View} failed and stays pending.",
                key.Annotation.ToString(),
                key.OrganizationName,
                key.ProjectName,
                key.ViewName);
            throw;
        }

        var partitionKey = key.GetCosmosPartitionKey();

        if (rowToken is not null)
        {
            // a soft-deleted row is not rewritten, its expiry stays; it keeps the token for the request after a restore
            await RemovePendingInvalidationAsync(
                repository,
                GetConfigurationId(key),
                partitionKey,
                $"FROM c WHERE c.{nameof(ConfigurationEntity.PendingInvalidationId)} = '{rowToken}' AND NOT IS_DEFINED(c.{nameof(ConfigurationEntity.AnnotationDeletionId)})");
        }

        foreach (var clear in clears)
        {
            await RemovePendingInvalidationAsync(
                repository,
                clear.Id,
                partitionKey,
                $"FROM c WHERE c.{nameof(ConfigurationHistoryEntity.PendingInvalidationId)} = '{clear.Token}'");
        }
    }

    // A newer write or clear sets its own token, so the filter never removes one whose invalidation is still owed.
    private async Task RemovePendingInvalidationAsync(IContainerRepository repository, string id, PartitionKey partitionKey, string filterPredicate)
    {
        using var response = await repository.Container.PatchItemStreamAsync(
            id,
            partitionKey,
            [PatchOperation.Remove($"/{nameof(ConfigurationEntity.PendingInvalidationId)}")],
            new PatchItemRequestOptions { FilterPredicate = filterPredicate, EnableContentResponseOnWrite = false });

        // a newer token (412) or a deleted item (404) is expected; anything else is logged, the work itself is done
        if (!response.IsSuccessStatusCode
            && response.StatusCode is not (HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound))
        {
            _logger.LogWarning(
                "Removing the pending invalidation of {Id} failed with {StatusCode}; a later request repeats the invalidation.",
                id,
                response.StatusCode);
        }
    }

    // The archives of the configuration whose clear has not finished its invalidation.
    private static async Task<List<PendingClear>> ReadPendingClearsAsync(FullKey key, IContainerRepository repository)
    {
        if (key.Annotation.Type == AnnotationType.UnitOfExecution)
        {
            // its clears store no token
            return [];
        }

        var query = new QueryDefinition(
                "SELECT c.id, c.PendingInvalidationId, c.Version FROM c "
                + "WHERE STARTSWITH(c.id, @historyPrefix) AND IS_STRING(c.PendingInvalidationId)")
            .WithParameter("@historyPrefix", $"{key.ViewName}.{EntityIdPrefixTypes.ConfigurationVersion}.{key.Annotation}.");

        using var iterator = repository.Container.GetItemQueryIterator<PendingInvalidationRow>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = key.GetCosmosPartitionKey() });

        var clears = new List<PendingClear>();

        while (iterator.HasMoreResults)
        {
            foreach (var row in await iterator.ReadNextAsync())
            {
                if (row.Id is not null && row.PendingInvalidationId is not null)
                {
                    clears.Add(new PendingClear(row.Id, row.PendingInvalidationId, row.Version));
                }
            }
        }

        return clears;
    }

    private static ConfigurationHistoryEntity ToHistory(FullKey key, ConfigurationEntity current, ulong version, string? pendingInvalidationId)
    {
        return new ConfigurationHistoryEntity
        {
            PartitionKey = key.GetPartitionKey(),
            Id = GetHistoryId(key, version),
            AnnotationKey = key.Annotation.ToString(),
            Version = version,
            Name = current.Name,
            ProjectName = current.ProjectName,
            ViewName = current.ViewName,
            Content = current.Content,
            Modified = current.Modified,
            PendingInvalidationId = pendingInvalidationId,
        };
    }

    private static string GetConfigurationId(FullKey key)
    {
        return $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{key.Annotation}";
    }

    private static string GetHistoryId(FullKey key, ulong version)
    {
        return $"{key.ViewName}.{EntityIdPrefixTypes.ConfigurationVersion}.{key.Annotation}.{version}";
    }

    // The token of a pending invalidation. A unit of execution has no descendants, so its writes and clears leave no
    // invalidation to finish and get no token.
    private static string? NewInvalidationId(FullKey key)
    {
        return key.Annotation.Type == AnnotationType.UnitOfExecution ? null : Guid.NewGuid().ToString("N");
    }

    // The size of a document in a batch; its content dominates, the rest is a small allowance.
    private static long EstimateBytes(JObject content)
    {
        return Encoding.UTF8.GetByteCount(content.ToString(Formatting.None)) + 1024;
    }

    private static ConflictException ConcurrentChange(string annotationKey)
    {
        return new ConflictException($"The configuration '{annotationKey}' was changed concurrently, try again.", ErrorCodes.Conflict);
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

    // How a write archives the stored version: Version is the archived version (null for a cache row, which has none),
    // IsStored that an equal archive exists already, NextVersion the version the write stores.
    private sealed record class ArchivePlan(ulong? Version, bool IsStored, ulong NextVersion);

    // The archive of a clear or delete, with the token of its pending cache invalidation (none for a unit of execution).
    private sealed record class PendingClear(string Id, string? Token, ulong Version);
}
