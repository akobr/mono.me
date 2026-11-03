using _42.Platform.Storyteller.Sdk.Configuring.Engine;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Storyteller.Sdk.Configuring.Container;

// The engines of a configuration, found by walking its providers, chained configurations included. Walked again on
// each call, so a rebuilt ConfigurationManager is followed.
internal static class StorytellerEngines
{
    public static IReadOnlyList<StorytellerConfigurationEngine> Find(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var engines = new List<StorytellerConfigurationEngine>();
        Visit(configuration, engines, new HashSet<IConfiguration>(ReferenceEqualityComparer.Instance));
        return engines;
    }

    private static void Visit(IConfiguration configuration, List<StorytellerConfigurationEngine> engines, HashSet<IConfiguration> visited)
    {
        // A section of a chained configuration does not expose its root.
        if (configuration is not IConfigurationRoot root || !visited.Add(root))
        {
            return;
        }

        foreach (var provider in root.Providers)
        {
            switch (provider)
            {
                // Two builds of one source share an engine.
                case StorytellerConfigurationProvider storyteller when !engines.Contains(storyteller.Engine):
                    engines.Add(storyteller.Engine);
                    break;

                case ChainedConfigurationProvider chained:
                    Visit(chained.Configuration, engines, visited);
                    break;
            }
        }
    }
}
