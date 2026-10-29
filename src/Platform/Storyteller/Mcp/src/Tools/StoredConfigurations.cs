using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The annotation's own stored document. The plain read returns the effective document (ancestors and templates
// merged); its Version is the version of the own document, which the versions route returns as stored. Version 0 means
// nothing is stored on the annotation.
public static class StoredConfigurations
{
    public static async Task<StoredConfiguration> GetOwnAsync(
        Sdk.IConfigurationsApiClient configurations,
        string organization,
        string project,
        string view,
        string key,
        CancellationToken cancellationToken)
    {
        var effective = await configurations.GetConfigurationAsync(organization, project, view, key, null, cancellationToken);

        if (effective.Version <= 0)
        {
            return new StoredConfiguration(0, null);
        }

        var stored = await configurations.GetConfigurationVersionAsync(organization, project, view, key, checked((int)effective.Version), cancellationToken);
        return new StoredConfiguration(effective.Version, ToolResults.ToToken(stored.Content) as JObject ?? new JObject());
    }
}
