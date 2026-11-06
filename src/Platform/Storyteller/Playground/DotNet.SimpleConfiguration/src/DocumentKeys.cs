using _42.Platform.Storyteller.Sdk.Configuring;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleConfiguration;

// The keys that came from the platform, without those of the environment and appsettings.json, each with the value the
// application reads: an environment variable still overrides the document. Keys compare without case, as IConfiguration
// compares them.
public static class DocumentKeys
{
    public static IReadOnlyDictionary<string, string> Read(IConfiguration configuration)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in ((IConfigurationRoot)configuration).Providers.OfType<StorytellerConfigurationProvider>())
        {
            Walk(provider, null, configuration, keys);
        }

        return keys;
    }

    private static void Walk(StorytellerConfigurationProvider provider, string? parent, IConfiguration configuration, Dictionary<string, string> keys)
    {
        // GetChildKeys returns one segment per key below the parent, so a section comes once per key it holds.
        foreach (var segment in provider.GetChildKeys([], parent).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = parent is null ? segment : ConfigurationPath.Combine(parent, segment);

            if (provider.TryGet(key, out _))
            {
                keys[key] = configuration[key] ?? string.Empty;
            }

            Walk(provider, key, configuration, keys);
        }
    }
}
