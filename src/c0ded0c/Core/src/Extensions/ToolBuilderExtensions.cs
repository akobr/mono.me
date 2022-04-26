using System.Collections.Generic;
using System.Linq;
using c0ded0c.Core.Configuration;
using c0ded0c.Core.Genesis;
using Microsoft.Extensions.Configuration;

namespace c0ded0c.Core
{
    public static class ToolBuilderExtensions
    {
        public static IToolBuilder UseCoreGenesis(this IToolBuilder builder)
        {
            return builder.ConfigureGenesisEngine(
                genesisBuilder => genesisBuilder.Use<CoreStructureGenesisMiddleware>());
        }

        public static IToolBuilder UseHashMap(this IToolBuilder builder)
        {
            return builder.ConfigureGenesisEngine(
                genesisBuilder => genesisBuilder.Use<HashMapGenesisMiddleware>());
        }

        public static IToolBuilder UseSearchIndex(this IToolBuilder builder)
        {
            return builder.ConfigureGenesisEngine(
                genesisBuilder => genesisBuilder.Use<SearchIndexGenesisMiddleware>());
        }

        public static IToolBuilder Configure(this IToolBuilder builder, IEnumerable<(string Key, string Value)> properties)
        {
            return builder.Configure((toolProperties)
                => toolProperties.SetItems(
                    properties.Select((p) => new KeyValuePair<string, string>(p.Key, p.Value))));
        }

        public static IToolBuilder Configure(this IToolBuilder builder, IToolConfiguration configuration)
        {
            return builder.Configure((toolProperties) => ToolConfigurationReader.SetTo(toolProperties, configuration));
        }

        public static IToolBuilder Configure(this IToolBuilder builder, IConfiguration configuration)
        {
            return builder.Configure((properties) =>
            {
                foreach (IConfigurationSection section in configuration.GetChildren())
                {
                    properties = properties.SetItem(section.GetFullKey(), section.Value);
                }

                return properties;
            });
        }
    }
}
