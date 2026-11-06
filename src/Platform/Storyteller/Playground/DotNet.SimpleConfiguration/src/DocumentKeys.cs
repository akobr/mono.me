using _42.Platform.Storyteller.Sdk.Configuring;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Storyteller.Playground.DotNet.SimpleConfiguration;

// The keys and values that came from the platform, without those of the environment and appsettings.json. Keys compare
// without case, as IConfiguration compares them. A value is the document's, even when an environment variable overrides
// it for the application: reading it through IConfiguration would let a key of the document print any variable of the
// process, the machine's API key included.
public static class DocumentKeys
{
    public static IReadOnlyDictionary<string, string> Read(IConfiguration configuration)
    {
        var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in ((IConfigurationRoot)configuration).Providers.OfType<StorytellerConfigurationProvider>())
        {
            Walk(provider, null, keys);
        }

        return keys;
    }

    private static void Walk(StorytellerConfigurationProvider provider, string? parent, Dictionary<string, string> keys)
    {
        // GetChildKeys returns one segment per key below the parent, so a section comes once per key it holds.
        foreach (var segment in provider.GetChildKeys([], parent).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = parent is null ? segment : ConfigurationPath.Combine(parent, segment);

            if (provider.TryGet(key, out var value))
            {
                keys[key] = value ?? string.Empty;
            }

            Walk(provider, key, keys);
        }
    }
}
