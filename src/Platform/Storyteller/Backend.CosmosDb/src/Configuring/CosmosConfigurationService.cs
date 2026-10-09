using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
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

public class CosmosConfigurationService : IConfigurationService
{
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IConfigurationBindingResolver? _bindingResolver;
    private readonly IConfigurationSchemaService? _schemaService;
    private readonly IJsonSerializationSettingsProvider _jsonSettingsProvider;
    private readonly JsonSerializerSettings _serializerOptions;
    private readonly JsonContentDiffer _differ;

    public CosmosConfigurationService(
        IContainerRepositoryProvider repositoryProvider,
        IJsonSerializationSettingsProvider jsonSettingsProvider,
        IOptions<JsonSerializerSettings> serializerOptions,
        IConfigurationBindingResolver bindingResolver = null,
        IConfigurationSchemaService schemaService = null)
    {
        _repositoryProvider = repositoryProvider;
        _bindingResolver = bindingResolver;
        _schemaService = schemaService;
        _jsonSettingsProvider = jsonSettingsProvider;
        _serializerOptions = serializerOptions.Value;
        _differ = new JsonContentDiffer(jsonSettingsProvider);
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
        return configuration is not null && configuration.Content.HasValues;
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
        var text = "SELECT c.AnnotationKey, c.Version, c.Author, c.CalculatedContentHash, c._ts, "
            + "(IS_OBJECT(c.Content) AND ARRAY_LENGTH(ObjectToArray(c.Content)) > 0) AS HasContent "
            + "FROM c WHERE STARTSWITH(c.PartitionKey, @projectPrefix) AND STARTSWITH(c.id, @idPrefix)";

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
                Author = item.Row.Author,
                Hash = item.Row.CalculatedContentHash,
                UpdatedAt = ListingTimestamps.ToUpdatedAt(item.Row.Timestamp),
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

        if (configuration is null)
        {
            var annotationExist = await repository.Container.ExistsAsync(key);
            if (!annotationExist)
            {
                return null;
            }
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
            : calculatedConfig.ToConfiguration(key.Annotation);
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

    public async Task<Configuration?> GetConfigurationHierarchyViewAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var annotationExist = await repository.Container.ExistsAsync(key);
        if (!annotationExist)
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
            : result.ToConfiguration(key.Annotation);
    }

    public async Task<IReadOnlyCollection<ConfigurationVersion>> GetConfigurationVersionsAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
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

    public async Task<Configuration> CreateOrUpdateConfigurationAsync(FullKey key, JObject value, string author, bool force = false)
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

        if (existingConfiguration is null)
        {
            // create a new configuration if there is nothing yet
            value = value.RemoveRequested();
            value = await value.ApplyPatchRequested();

            if (!force && _schemaService is not null && value.HasValues)
            {
                await _schemaService.ValidateContentAsync(key.OrganizationName, key.ProjectName, key.ViewName, annotationKey, value);
            }

            var maxVersionResponse = await repository.Container.GetItemLinqQueryable<ConfigurationHistoryEntity>(
                requestOptions: new QueryRequestOptions { PartitionKey = partitionKey })
                .Where(history => history.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.ConfigurationVersion}.{annotationKey}."))
                .Select(history => history.Version)
                .MaxAsync();

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
                Version = maxVersionResponse.Resource + 1,
                Author = author,
            };

            await repository.Container.CreateItemAsync(configuration, partitionKey);
            return configuration.ToConfiguration();
        }

        var newContent = value;

        // merge into stored content when there is some; an empty document is replaced
        if (existingConfiguration.Content.HasValues)
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
                CreationTime = existingConfiguration.GetLastUpdatedTime(),
                Author = existingConfiguration.Author,
            };

            await repository.Container.CreateItemAsync(history, partitionKey);
        }

        // invalidate all ancestor configurations
        await InvalidateConfigurationsAsync(key, repository);

        // save a new version of the configuration
        var newConfiguration = existingConfiguration with
        {
            Version = existingConfiguration.Version + 1,
            Content = newContent,
            Author = author,
            CalculatedContent = null,
            CalculatedContentHash = null,
        };
        await repository.Container.UpsertItemAsync(newConfiguration, partitionKey);

        return newConfiguration.ToConfigurationFromContent();
    }

    public async Task<Configuration> PatchConfigurationAsync(FullKey key, JArray patchOperations, string author, bool force = false)
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
            CreationTime = existingConfiguration.GetLastUpdatedTime(),
            Author = existingConfiguration.Author,
        };

        var newConfiguration = existingConfiguration with
        {
            Version = existingConfiguration.Version + 1,
            Content = newContent,
            Author = author,
            CalculatedContent = null,
            CalculatedContentHash = null,
        };

        var batch = repository.Container.CreateTransactionalBatch(partitionKey);
        batch.CreateItem(history);
        batch.UpsertItem(newConfiguration);
        await batch.ExecuteAsync();

        return newConfiguration.ToConfigurationFromContent();
    }

    public async Task ClearConfigurationAsync(FullKey key)
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
            CreationTime = existingConfiguration.GetLastUpdatedTime(),
            Author = existingConfiguration.Author,
        };

        var transaction = repository.Container.CreateTransactionalBatch(partitionKey);
        transaction.CreateItem(history);
        transaction.DeleteItem(id);
        await transaction.ExecuteAsync();
        await InvalidateConfigurationsAsync(key, repository);
    }

    public async Task DeleteAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        await ForceDeleteConfigurationAsync(key, repository);
        await InvalidateConfigurationsAsync(key, repository);
    }

    public async Task DeleteWithDescendantsAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);

        switch (key.Annotation.Type)
        {
            case AnnotationType.Responsibility:
            {
                // delete all units, usages, executions, unit-of-executions, and the responsibility (one responsibility partition)
                // delete all configuration in one responsibility partition
                await DeleteConfigurationsAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}."),
                    key.GetCosmosPartitionKey());
                return;
            }

            case AnnotationType.Unit:
            {
                // delete all unit-of-executions, and the unit (one responsibility partition)
                await DeleteConfigurationsAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && ((config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.UnitOfExecution}.") && config.Id.EndsWith($".{key.Annotation.UnitName}"))
                            || config.Id == key.Annotation.ToString()),
                    key.GetCosmosPartitionKey());
                return;
            }

            case AnnotationType.Usage:
            {
                // delete all executions, unit-of-executions, and the usage (one responsibility partition)
                await DeleteConfigurationsAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && (config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Execution}.{key.Annotation.SubjectName}.")
                            || config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.UnitOfExecution}.{key.Annotation.SubjectName}.")
                            || config.Id == key.Annotation.ToString()),
                    key.GetCosmosPartitionKey());
                return;
            }

            case AnnotationType.Subject:
            {
                // delete all usages, contexts, executions, unit-of-executions, and the subject
                // delete the configurations in the responsibility partitions
                await DeleteConfigurationsAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && (config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Usage}.{key.Annotation.SubjectName}.")
                            || config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Execution}.{key.Annotation.SubjectName}.")
                            || config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.UnitOfExecution}.{key.Annotation.SubjectName}.")));

                // delete the configurations in the subject partition
                await DeleteConfigurationsAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && (config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Context}.")
                            || config.Id == key.Annotation.ToString()),
                    key.GetCosmosPartitionKey());
                return;
            }

            case AnnotationType.Context:
            {
                // delete all executions, unit-of-executions, and the context
                // delete the configurations in the responsibility partitions
                await DeleteConfigurationsAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && ((config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Execution}.{key.Annotation.SubjectName}.") && config.Id.EndsWith($".{key.Annotation.ContextName}"))
                            || (config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.UnitOfExecution}.{key.Annotation.SubjectName}.") && config.Id.Contains($".{key.Annotation.ContextName}."))));
                // delete the context (in subject partition)
                await ForceDeleteConfigurationAsync(key, repository);
                return;
            }

            case AnnotationType.Execution:
            {
                // delete all unit-of-executions and the execution (one responsibility partition)
                await DeleteConfigurationsAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && (config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.UnitOfExecution}.{key.Annotation.SubjectName}.{key.Annotation.ResponsibilityName}.{key.Annotation.ContextName}.")
                            || config.Id == key.Annotation.ToString()),
                    key.GetCosmosPartitionKey());
                return;
            }

            case AnnotationType.UnitOfExecution:
            {
                // delete only the unit-of-execution (it is the deepest configuration)
                await ForceDeleteConfigurationAsync(key, repository);
                return;
            }

            default:
            {
                throw new ArgumentOutOfRangeException(nameof(key.Annotation.Type));
            }
        }
    }

    private async Task<Configuration?> GetResolvedConfigurationInternalAsync(FullKey key, bool includeSecrets)
    {
        var config = await GetRawConfigurationAsync(key);

        if (config is null)
        {
            return null;
        }

        if (_bindingResolver is null)
        {
            return config;
        }

        var scope = new BindingScope
        {
            Document = config.Content.DeepClone(),
            Context = new ConfigurationBindingContext(key),
        };

        await _bindingResolver.ResolveAsync(config.Content, includeSecrets, scope);
        return config;
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
                        Author = "system",
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
            CreationTime = existingConfiguration.GetLastUpdatedTime(),
            Author = existingConfiguration.Author,
        };
        transaction.CreateItem(history);
        transaction.DeleteItem(id);
        await transaction.ExecuteAsync();
    }

    private static Task DeleteConfigurationsAsync(
        IContainerRepository repository,
        Expression<Func<ConfigurationEntity, bool>> predicate)
    {
        var queryable = repository.Container.GetItemLinqQueryable<ConfigurationEntity>(
            allowSynchronousQueryExecution: true);

        var groups = queryable
            .Where(predicate)
            .ToList()
            .GroupBy(config => config.PartitionKey);

        var transactionTasks = new List<Task>();

        foreach (var group in groups)
        {
            var partitionKey = new PartitionKey(group.Key);
            var batchItemCount = 0;
            var batch = repository.Container.CreateTransactionalBatch(partitionKey);
            foreach (var configEntity in group)
            {
                if (configEntity.Content.HasValues)
                {
                    var historyVersion = configEntity.Version;
                    var historyKey = $"{EntityIdPrefixTypes.ConfigurationVersion}.{configEntity.AnnotationKey}.{historyVersion}";
                    var historyId = $"{configEntity.ViewName}.{historyKey}";

                    ++batchItemCount;
                    batch.CreateItem(new ConfigurationHistoryEntity
                    {
                        PartitionKey = group.Key,
                        Id = historyId,
                        AnnotationKey = configEntity.AnnotationKey,
                        Version = historyVersion,
                        Name = configEntity.Name,
                        ProjectName = configEntity.ProjectName,
                        ViewName = configEntity.ViewName,
                        Content = configEntity.Content,
                        CreationTime = configEntity.GetLastUpdatedTime(),
                        Author = configEntity.Author,
                    });
                }

                ++batchItemCount;
                batch.DeleteItem(configEntity.Id);
            }

            if (batchItemCount > 0)
            {
                transactionTasks.Add(batch.ExecuteAsync());
            }
        }

        return Task.WhenAll(transactionTasks);
    }

    private static async Task DeleteConfigurationsAsync(
        IContainerRepository repository,
        Expression<Func<ConfigurationEntity, bool>> predicate,
        PartitionKey partitionKey)
    {
        var queryable = repository.Container.GetItemLinqQueryable<ConfigurationEntity>(
            requestOptions: new QueryRequestOptions
            {
                PartitionKey = partitionKey,
                MaxItemCount = CosmosConstants.MaxItemCountPerPage,
            });

        var feed = queryable
            .Where(predicate)
            .ToFeedIterator();

        var batchItemCount = 0;
        var batch = repository.Container.CreateTransactionalBatch(partitionKey);
        while (feed.HasMoreResults)
        {
            var ids = await feed.ReadNextAsync();
            foreach (var configEntity in ids)
            {
                if (configEntity.Content.HasValues)
                {
                    var historyVersion = configEntity.Version;
                    var historyKey = $"{EntityIdPrefixTypes.ConfigurationVersion}.{configEntity.AnnotationKey}.{historyVersion}";
                    var historyId = $"{configEntity.ViewName}.{historyKey}";

                    ++batchItemCount;
                    batch.CreateItem(new ConfigurationHistoryEntity
                    {
                        PartitionKey = configEntity.PartitionKey,
                        Id = historyId,
                        AnnotationKey = configEntity.AnnotationKey,
                        Version = historyVersion,
                        Name = configEntity.Name,
                        ProjectName = configEntity.ProjectName,
                        ViewName = configEntity.ViewName,
                        Content = configEntity.Content,
                        CreationTime = configEntity.GetLastUpdatedTime(),
                        Author = configEntity.Author,
                    });
                }

                ++batchItemCount;
                batch.DeleteItem(configEntity.Id);
            }
        }

        if (batchItemCount > 0)
        {
            await batch.ExecuteAsync();
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
