using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;
using _42.Platform.Storyteller.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

// The composition of the effective configuration: the walk of CalculateAndCacheConfigurationAsync over the stored
// documents, read-only. It never reads a cached calculation, never writes and creates no cache item. Each merge of a
// stored document is a step, and the marks of ConfigurationCompositionOrigins carry the step of every value through
// the real merge.
public partial class CosmosConfigurationService
{
    public async Task<ConfigurationComposition?> GetConfigurationCompositionAsync(FullKey key)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var walk = new CompositionWalk(repository);
        var configuration = await ReadCompositionConfigurationAsync(key, walk);

        // the rules of GetRawConfigurationAsync: an existing item is composed without a state check
        if (configuration?.AnnotationDeletionId is not null)
        {
            return null;
        }

        if (configuration is null
            && await AnnotationItemStore.ReadStateAsync(repository.Container, key) != AnnotationItemState.Active)
        {
            return null;
        }

        var (composed, _) = await ComposeNodeAsync(BuildInheritanceGraph(key), 0, walk);
        var content = ConfigurationCompositionOrigins.Strip(composed);
        string? contentHash = null;

        // the hash of the Effective calculation, which has none for an empty document
        if (content.HasValues)
        {
            var hash = content.CalculateMurmurHash32Bits(_jsonSettingsProvider.GetSettings(JsonSettingNames.Unique));
            contentHash = $"{hash:x8}";
        }

        return new ConfigurationComposition
        {
            AnnotationKey = key.Annotation.ToString(),
            Content = content,
            Hash = contentHash,
            Sources = walk.Sources,
            Nodes = walk.Nodes,
            Steps = walk.Steps,
            Origins = ConfigurationCompositionOrigins.Collect(composed),
        };
    }

    // One calculation of CalculateAndCacheConfigurationAsync: the finished document of each parent in GetAncestors()
    // order, the template of the type, then the own configuration. A shared ancestor is composed again in every branch.
    private async Task<(JObject Content, int Node)> ComposeNodeAsync(InheritanceGraphNode node, int depth, CompositionWalk walk)
    {
        var config = new JObject();
        var parentNodes = new List<int>(2);

        foreach (var ancestorNode in node.GetAncestors())
        {
            var (parentConfig, parentNode) = await ComposeNodeAsync(ancestorNode, depth + 1, walk);
            config.MergeInto(parentConfig);
            parentNodes.Add(parentNode);
        }

        var key = node.Key;
        var index = walk.AddNode(key.Annotation.ToString(), depth, parentNodes);

        // a template step only when the view has a template for the type
        var template = await ReadCompositionTemplateAsync(key, walk);
        if (template is not null)
        {
            var templateSource = ToTemplateSource(key, template);
            var templateStep = walk.AddStep(templateSource, index);
            config.MergeInto(ConfigurationCompositionOrigins.Mark(template.Content, templateStep));
        }

        // a configuration step always, so the whole lineage shows; a missing configuration applies nothing
        var configurationSource = ToConfigurationSource(key, await ReadCompositionConfigurationAsync(key, walk));
        var configurationStep = walk.AddStep(configurationSource, index);
        if (configurationSource.Content is not null)
        {
            config.MergeInto(ConfigurationCompositionOrigins.Mark(configurationSource.Content, configurationStep));
        }

        return (config, index);
    }

    // Each configuration item is read once per request, a shared node reuses the read.
    private async Task<ConfigurationEntity?> ReadCompositionConfigurationAsync(FullKey key, CompositionWalk walk)
    {
        var annotationKey = key.Annotation.ToString();

        if (!walk.Configurations.TryGetValue(annotationKey, out var configuration))
        {
            configuration = await walk.Repository.Container.TryReadItemAsync(
                $"{key.ViewName}.{EntityIdPrefixTypes.Configuration}.{annotationKey}",
                key.GetCosmosPartitionKey(),
                stream => stream.DeserializeNewtonsoft<ConfigurationEntity>(_serializerOptions));
            walk.Configurations[annotationKey] = configuration;
        }

        return configuration;
    }

    // Each template of the view is read once per request, as in the Effective calculation.
    private async Task<GenerateTemplateEntity?> ReadCompositionTemplateAsync(FullKey key, CompositionWalk walk)
    {
        var typeCode = key.Annotation.TypeCode.ToLowerInvariant();

        if (!walk.Templates.TryGetValue(typeCode, out var template))
        {
            (template, _) = await ReadTemplateAsync(key, typeCode, walk.Repository);
            walk.Templates[typeCode] = template;
        }

        return template;
    }

    private static ConfigurationCompositionSource ToConfigurationSource(FullKey key, ConfigurationEntity? configuration)
    {
        var annotationKey = key.Annotation.ToString();
        var source = new ConfigurationCompositionSource
        {
            Id = $"configuration:{annotationKey}",
            Kind = ConfigurationCompositionSourceKind.Configuration,
            AnnotationType = key.Annotation.Type,
            AnnotationKey = annotationKey,
            Presence = ConfigurationCompositionSourcePresence.Missing,
            Version = 0,
        };

        // every write gives version 1 or more and a clear deletes the item, so version 0 without content is an item
        // that holds only the cache of an Effective read, missing as no item at all
        if (configuration is null
            || (configuration.Version == 0 && !configuration.Content.HasValues))
        {
            return source;
        }

        return source with
        {
            Presence = configuration.Content.HasValues
                ? ConfigurationCompositionSourcePresence.Present
                : ConfigurationCompositionSourcePresence.Empty,
            Version = configuration.Version,
            Modified = configuration.Modified,
            Content = configuration.Content,
        };
    }

    private static ConfigurationCompositionSource ToTemplateSource(FullKey key, GenerateTemplateEntity template)
    {
        return new ConfigurationCompositionSource
        {
            Id = $"template:{key.Annotation.TypeCode.ToLowerInvariant()}",
            Kind = ConfigurationCompositionSourceKind.Template,
            AnnotationType = key.Annotation.Type,
            Presence = template.Content.HasValues
                ? ConfigurationCompositionSourcePresence.Present
                : ConfigurationCompositionSourcePresence.Empty,
            Version = template.Version,
            Modified = template.Modified,
            Content = template.Content,
        };
    }

    // The reads and the parts of one composition.
    private sealed class CompositionWalk(IContainerRepository repository)
    {
        private readonly HashSet<string> _sourceIds = [];

        public IContainerRepository Repository { get; } = repository;

        // Configuration items by annotation key, null for no item.
        public Dictionary<string, ConfigurationEntity?> Configurations { get; } = [];

        // Templates of the view by type code, null for no template.
        public Dictionary<string, GenerateTemplateEntity?> Templates { get; } = [];

        public List<ConfigurationCompositionSource> Sources { get; } = [];

        public List<ConfigurationCompositionNode> Nodes { get; } = [];

        public List<ConfigurationCompositionStep> Steps { get; } = [];

        // A node takes the next position once its parents are composed, so the nodes are listed in the order their
        // calculations finish; the parents learn which node merges them.
        public int AddNode(string annotationKey, int depth, IEnumerable<int> parentNodes)
        {
            var index = Nodes.Count;
            Nodes.Add(new ConfigurationCompositionNode { Index = index, AnnotationKey = annotationKey, Depth = depth });

            foreach (var parentNode in parentNodes)
            {
                Nodes[parentNode] = Nodes[parentNode] with { MergedInto = index };
            }

            return index;
        }

        // A source is listed once, at its first application; returns the order of the step.
        public int AddStep(ConfigurationCompositionSource source, int node)
        {
            if (_sourceIds.Add(source.Id))
            {
                Sources.Add(source);
            }

            var order = Steps.Count + 1;
            Steps.Add(new ConfigurationCompositionStep { Order = order, SourceId = source.Id, Node = node });
            return order;
        }
    }
}
