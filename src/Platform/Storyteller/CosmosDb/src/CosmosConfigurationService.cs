using System;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Entities;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

public class CosmosConfigurationService : IConfigurationService
{
    private readonly IContainerRepositoryProvider _repositoryProvider;

    public CosmosConfigurationService(IContainerRepositoryProvider repositoryProvider)
    {
        _repositoryProvider = repositoryProvider;
    }

    public async Task<JObject?> GetConfigurationAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var configurationKey = $"cnf.{key.Annotation}";
        var id = $"{key.ViewName}.{configurationKey}";
        var partitionKey = key.GetCosmosPartitionKey();

        var configuration = await repository.Container.TryReadItem<Configuration>(id, partitionKey);

        if (configuration?.CalculatedContent is not null)
        {
            return configuration.CalculatedContent;
        }

        var node = BuildInheritanceGraph(key);
        var calculatedConfig = await CalculateAndCacheConfigurationAsync(node, repository);
        return calculatedConfig;
    }

    public async Task<JObject> CreateOrUpdateConfigurationAsync(FullKey key, JObject value)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var configurationKey = $"cnf.{key.Annotation}";
        var id = $"{key.ViewName}.{configurationKey}";
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);

        var existingConfiguration = await repository.Container.TryReadItem<Configuration>(id, partitionKey);

        if (existingConfiguration is null)
        {
            var configuration = new Configuration
            {
                PartitionKey = partitionKeyValue,
                AnnotationKey = configurationKey,
                IsServerSubstitutionDisabled = false,
                Name = key.Annotation.Name,
                ProjectName = key.ProjectName,
                ViewName = key.ViewName,
                Content = value,
            };

            await repository.Container.CreateItemAsync(configuration, partitionKey);
            return value;
        }

        var mergedConfigs = existingConfiguration.Content;
        mergedConfigs.MergeInto(value);
        var newConfiguration = existingConfiguration with { Content = mergedConfigs };
        // TODO: [P1] invalidate all cached configurations
        await repository.Container.UpsertItemAsync(newConfiguration, partitionKey);
        return mergedConfigs;
    }

    public async Task<bool> DeleteConfigurationAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var configurationKey = $"cnf.{key.Annotation}";
        var id = $"{key.ViewName}.{configurationKey}";
        var partitionKey = key.GetCosmosPartitionKey();

        // TODO: [P1] invalidate all cached configurations
        var response = await repository.Container.DeleteItemAsync<Configuration>(id, partitionKey);
        return response.StatusCode != HttpStatusCode.NotFound;
    }

    private async Task<JObject?> CalculateAndCacheConfigurationAsync(InheritanceGraphNode node, IContainerRepository repository)
    {
        var key = node.Key;
        var config = new JObject();

        // Fill auto-generated properties (if any)
        await TryAutogeneratePropertiesAsync(config, key, repository);

        // Inherit parent configuration (responsibility <|- unit <|- subject <|- usage <|- instance <|- execution) as graph
        foreach (var ancestorNode in node.GetAncestors())
        {
            var parentConfig = await CalculateAndCacheConfigurationAsync(ancestorNode, repository);
            if (parentConfig is not null)
            {
                config.MergeInto(parentConfig);
            }
        }

        // The most specific configuration has precedence, is merged last
        var partitionKeyValue = key.GetPartitionKey();
        var partitionKey = new PartitionKey(partitionKeyValue);
        var configEntry = await repository.Container.TryReadItem<Configuration>(key.GetCosmosItemKey(), partitionKey);
        var exist = configEntry is not null;

        if (exist)
        {
            config.MergeInto(configEntry!.Content);

            if (config.HasValues)
            {
                configEntry = configEntry with { CalculatedContent = config };
                await repository.Container.UpsertItemAsync(configEntry, partitionKey);
            }
        }
        else if (config.HasValues)
        {
            await repository.Container.CreateItemAsync(
                new Configuration
                {
                    PartitionKey = partitionKeyValue,
                    AnnotationKey = $"cnf.{key.Annotation}",
                    IsServerSubstitutionDisabled = false,
                    Name = key.Annotation.Name,
                    ProjectName = key.ProjectName,
                    ViewName = key.ViewName,
                    Content = new JObject(),
                    CalculatedContent = config,
                },
                partitionKey);
        }

        return config;
    }

    private async Task TryAutogeneratePropertiesAsync(JObject config, FullKey key, IContainerRepository repository)
    {
        // TODO: [P2] Implement auto-generation of properties per type of annotation
        await Task.CompletedTask;
    }

    private static InheritanceGraphNode BuildInheritanceGraph(FullKey key)
    {
        var targetNode = new InheritanceGraphNode(key);
        var annotationKey = key.Annotation;

        switch (annotationKey.Type)
        {
            case AnnotationType.Responsibility:
            case AnnotationType.Subject:
                break;

            case AnnotationType.Job:
                targetNode.CreateAncestor(FullKey.Create(annotationKey.GetResponsibilityKey(), key));
                break;

            case AnnotationType.Usage:
                targetNode.CreateAncestor(FullKey.Create(annotationKey.GetResponsibilityKey(), key));
                targetNode.CreateAncestor(FullKey.Create(annotationKey.GetSubjectKey(), key));
                break;

            case AnnotationType.Context:
                targetNode.CreateAncestor(FullKey.Create(annotationKey.GetSubjectKey(), key));
                break;

            case AnnotationType.Execution:
                var responsibilityNode = targetNode.CreateAncestor(FullKey.Create(annotationKey.GetResponsibilityKey(), key));
                responsibilityNode.SetDescendant(FullKey.Create(annotationKey.GetUsageKey(), key));
                var subjectNode = targetNode.CreateAncestor(FullKey.Create(annotationKey.GetSubjectKey(), key));
                subjectNode.SetDescendant(FullKey.Create(annotationKey.GetContextKey(), key));
                break;

            default:
                throw new InvalidOperationException("Configuration of an unknown annotation type.");
        }

        return targetNode;
    }
}
