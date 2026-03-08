using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;
using _42.Platform.Storyteller.Json;
using AutoMapper;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
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
    private readonly IBindingExecutor? _bindingExecutor;
    private readonly IJsonSerializationSettingsProvider _jsonSettingsProvider;
    private readonly IMapper _mapper;
    private readonly JsonSerializerSettings _serializerOptions;

    public CosmosConfigurationService(
        IContainerRepositoryProvider repositoryProvider,
        IJsonSerializationSettingsProvider jsonSettingsProvider,
        IMapper mapper,
        IOptions<JsonSerializerSettings> serializerOptions,
        IBindingExecutor bindingExecutor = null)
    {
        _repositoryProvider = repositoryProvider;
        _bindingExecutor = bindingExecutor;
        _jsonSettingsProvider = jsonSettingsProvider;
        _mapper = mapper;
        _serializerOptions = serializerOptions.Value;
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

    public async Task<JObject?> GetRawConfigurationAsync(FullKey key)
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
            return configuration.CalculatedContent;
        }

        var node = BuildInheritanceGraph(key);
        var calculatedConfig = await CalculateAndCacheConfigurationAsync(node, repository);
        return calculatedConfig;
    }

    public Task<JObject?> GetResolvedConfigurationAsync(FullKey key, bool includeSecrets = false)
    {
        return GetResolvedConfigurationInternalAsync(key, includeSecrets);
    }

    public Task<JObject?> GetResolvedConfigurationWithSecretsAsync(FullKey key)
    {
        return GetResolvedConfigurationInternalAsync(key, true);
    }

    public Task<JObject?> GetResolvedConfigurationWithoutSecretsAsync(FullKey key)
    {
        return GetResolvedConfigurationInternalAsync(key, false);
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
            versions.AddRange(results.Select(entity => _mapper.Map<ConfigurationHistoryEntity, ConfigurationVersion>(entity)));
        }

        var configurationKey = $"{EntityIdPrefixTypes.Configuration}.{key.Annotation}";
        var configurationId = $"{key.ViewName}.{configurationKey}";

        var configuration = await repository.Container.TryReadItemAsync(
            configurationId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));

        if (configuration is not null)
        {
            versions.Add(_mapper.Map<ConfigurationEntity, ConfigurationVersion>(configuration));
        }

        return versions;
    }

    public async Task<JObject?> GetConfigurationVersionContentAsync(FullKey key, uint version)
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
            return versionEntity.Content;
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

        return configEntity.Content;
    }

    public Task<IReadOnlyCollection<string>> GetConfigurationVersionChangesAsync(FullKey key, uint version)
    {
        return GetConfigurationVersionChangesAsync(key, version - 1, version);
    }

    // TODO: [P2] put this into different library
    public Task<IReadOnlyCollection<string>> GetConfigurationVersionChangesAsync(FullKey key, uint fromVersion, uint toVersion)
    {
        return GetConfigurationChangesAsync(
            () => fromVersion == 0 ? Task.FromResult<JObject?>(new JObject()) : GetConfigurationVersionContentAsync(key, fromVersion),
            () => toVersion == 0 ? Task.FromResult<JObject?>(new JObject()) : GetConfigurationVersionContentAsync(key, toVersion),
            $"Unknown version {fromVersion} of the configuration for {key.Annotation}.",
            $"Unknown version {toVersion} of the configuration for {key.Annotation}.");
    }

    public Task<IReadOnlyCollection<string>> GetConfigurationViewChangesAsync(FullKey sourceKey, string toView)
    {
        var targetKey = FullKey.Create(sourceKey.Annotation, sourceKey.OrganizationName, sourceKey.ProjectName, toView);
        return GetConfigurationChangesAsync(
            () => GetRawConfigurationAsync(sourceKey),
            () => GetRawConfigurationAsync(targetKey),
            $"Unknown configuration for {sourceKey.Annotation} in view {sourceKey.ViewName}.",
            $"Unknown configuration for {sourceKey.Annotation} in view {toView}.");
    }

    private async Task<IReadOnlyCollection<string>> GetConfigurationChangesAsync(
        Func<Task<JObject?>> fromProvider,
        Func<Task<JObject?>> toProvider,
        string fromErrorMessage,
        string toErrorMessage)
    {
        var fromJson = await fromProvider();

        if (fromJson is null)
        {
            throw new InvalidOperationException(fromErrorMessage);
        }

        var toJson = await toProvider();

        if (toJson is null)
        {
            throw new InvalidOperationException(toErrorMessage);
        }

        var serializerSettings = _jsonSettingsProvider.GetSettings(JsonSettingNames.Unique);
        var fromText = !fromJson.HasValues ? string.Empty : JsonConvert.SerializeObject(fromJson, Formatting.Indented, serializerSettings);
        var toText = !toJson.HasValues ? string.Empty : JsonConvert.SerializeObject(toJson, Formatting.Indented, serializerSettings);

        var diff = InlineDiffBuilder.Diff(fromText, toText);
        var lines = new List<string>();

        foreach (var line in diff.Lines)
        {
            switch (line.Type)
            {
                case ChangeType.Inserted:
                    lines.Add($"+ {line.Text}");
                    break;
                case ChangeType.Deleted:
                    lines.Add($"- {line.Text}");
                    break;
                default:
                    lines.Add($"  {line.Text}");
                    break;
            }
        }

        return lines;
    }

    public async Task<JObject> CreateOrUpdateConfigurationAsync(FullKey key, JObject value, string author)
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
            value.RemoveRequested();

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
            return value;
        }

        var newContent = value;

        // store history version only if there was some content before
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
                Author = author,
            };

            await repository.Container.CreateItemAsync(history, partitionKey);
        }
        else // has some content before, merge must be done
        {
            newContent = existingConfiguration.Content;
            newContent.MergeInto(value);
            newContent.RemoveRequested();

            if (JToken.DeepEquals(existingConfiguration.Content, newContent))
            {
                // check for no change after merge
                return existingConfiguration.Content;
            }
        }

        // invalidate all ancestor configurations
        await InvalidateConfigurationsAsync(key, repository);

        // save a new version of the configuration
        var newConfiguration = existingConfiguration with
        {
            Version = existingConfiguration.Version + 1,
            Content = newContent,
            CalculatedContent = null,
            CalculatedContentHash = null,
        };
        await repository.Container.UpsertItemAsync(newConfiguration, partitionKey);

        return newContent;
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

    private async Task<JObject?> GetResolvedConfigurationInternalAsync(FullKey key, bool includeSecrets)
    {
        var content = await GetRawConfigurationAsync(key);

        if (content is null)
        {
            return null;
        }

        if (_bindingExecutor is null)
        {
            return content;
        }

        var queue = new Queue<JObject>();
        queue.Enqueue(content);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            foreach (var property in current.Properties())
            {
                switch (property.Type)
                {
                    case JTokenType.String:
                        await TryProcessDataBinding(property, includeSecrets);
                        break;

                    case JTokenType.Object:
                        // TODO: [P3] logic operations and templates processing
                        queue.Enqueue((JObject)property.Value);
                        break;

                    case JTokenType.Array:
                    {
                        var array = (JArray)property.Value;
                        foreach (var item in array)
                        {
                            switch (item.Type)
                            {
                                case JTokenType.String:
                                    await TryProcessDataBinding(property, includeSecrets);
                                    break;

                                case JTokenType.Object:
                                    queue.Enqueue((JObject)item);
                                    break;
                            }
                        }

                        break;
                    }
                }
            }
        }

        return content;
    }

    private ValueTask<bool> TryProcessDataBinding(JProperty property, bool includeSecrets)
    {
        if (property.Type != JTokenType.String)
        {
            return ValueTask.FromResult(false);
        }

        var rawValue = (string)property.Value;
        return rawValue[0] != '@'
            ? ValueTask.FromResult(false)
            : _bindingExecutor!.TryBinding(property, includeSecrets);
    }

    private async Task TryAutogeneratePropertiesAsync(JObject config, FullKey key, IContainerRepository repository)
    {
        // TODO: [P2] Implement auto-generation of properties per type of annotation
        await Task.CompletedTask;
    }

    private async Task<JObject?> CalculateAndCacheConfigurationAsync(InheritanceGraphNode node, IContainerRepository repository)
    {
        var key = node.Key;
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);
        var annotationKeyString = key.Annotation.ToString();
        var configEntryId = $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{annotationKeyString}";
        var configEntry = await repository.Container.TryReadItemAsync(
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

        // Fill auto-generated properties (if any) [not implemented yet]
        await TryAutogeneratePropertiesAsync(config, key, repository);

        // Inherit parent configurations (responsibility <|- unit <|- subject <|- usage <|- context <|- execution <|- unit-of-execution) as graph
        foreach (var ancestorNode in node.GetAncestors())
        {
            var parentConfig = await CalculateAndCacheConfigurationAsync(ancestorNode, repository);
            if (parentConfig is not null)
            {
                config.MergeInto(parentConfig);
            }
        }

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
                await repository.Container.UpsertItemAsync(configEntry, partitionKey);
            }
        }
        else if (config.HasValues)
        {
            var hash = config.CalculateMurmurHash32Bits(_jsonSettingsProvider.GetSettings(JsonSettingNames.Unique));
            await repository.Container.CreateItemAsync(
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
                partitionKey);
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
                await InvalidateConfigurationsAsync(
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
                await InvalidateConfigurationsAsync(
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
                await InvalidateConfigurationsAsync(
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
                await InvalidateConfigurationsAsync(
                    repository,
                    config =>
                        config.ProjectName == key.ProjectName
                        && config.ViewName == key.ViewName
                        && config.Id.StartsWith($"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{AnnotationTypeCodes.Context}."),
                    key.GetCosmosPartitionKey());

                // invalidate usages, executions, unit-of-executions in the responsibility partitions
                await InvalidateConfigurationsAsync(
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
                await InvalidateConfigurationsAsync(
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
                await InvalidateConfigurationsAsync(
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

    private static Task InvalidateConfigurationsAsync(
    IContainerRepository repository,
    Expression<Func<ConfigurationEntity, bool>> predicate)
    {
        var queryable = repository.Container.GetItemLinqQueryable<ConfigurationEntity>(
            allowSynchronousQueryExecution: true);

        var groups = queryable
            .Where(predicate)
            .Select(config => new EntityIndices { PartitionKey = config.PartitionKey, Id = config.Id })
            .ToList()
            .GroupBy(config => config.PartitionKey);

        var patchOperations = new List<PatchOperation>
        {
            PatchOperation.Remove($"/{nameof(ConfigurationEntity.CalculatedContent)}"),
            PatchOperation.Remove($"/{nameof(ConfigurationEntity.CalculatedContentHash)}"),
            PatchOperation.Increment($"/{nameof(ConfigurationEntity.AffectedCounter)}", 1),
        };

        var transactionTasks = new List<Task>();

        foreach (var group in groups)
        {
            var partitionKey = new PartitionKey(group.Key);
            var batchItemCount = 0;
            var batch = repository.Container.CreateTransactionalBatch(partitionKey);

            foreach (var indices in group)
            {
                ++batchItemCount;
                batch.PatchItem(indices.Id, patchOperations);
            }

            if (batchItemCount > 0)
            {
                transactionTasks.Add(batch.ExecuteAsync());
            }
        }

        return Task.WhenAll(transactionTasks);
    }

    private static async Task InvalidateConfigurationsAsync(
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
            .Select(config => config.Id)
            .ToFeedIterator();

        var patchOperations = new List<PatchOperation>
        {
            PatchOperation.Remove($"/{nameof(ConfigurationEntity.CalculatedContent)}"),
            PatchOperation.Remove($"/{nameof(ConfigurationEntity.CalculatedContentHash)}"),
            PatchOperation.Increment($"/{nameof(ConfigurationEntity.AffectedCounter)}", 1),
        };

        var patchItemCount = 0;
        var batch = repository.Container.CreateTransactionalBatch(partitionKey);
        while (feed.HasMoreResults)
        {
            var ids = await feed.ReadNextAsync();
            foreach (var id in ids)
            {
                ++patchItemCount;
                batch.PatchItem(id, patchOperations);
            }
        }

        if (patchItemCount > 0)
        {
            await batch.ExecuteAsync();
        }
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
}
