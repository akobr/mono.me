using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

public interface IConfigurationService
{
    Task<bool> HasConfigurationContentAsync(FullKey key);

    Task<Configuration?> GetRawConfigurationAsync(FullKey key);

    Task<Configuration?> GetResolvedConfigurationAsync(FullKey key, bool includeSecrets = false);

    Task<Configuration?> GetResolvedConfigurationWithSecretsAsync(FullKey key);

    Task<Configuration?> GetResolvedConfigurationWithoutSecretsAsync(FullKey key);

    Task<Configuration?> GetConfigurationHierarchyViewAsync(FullKey key);

    Task<Configuration> CreateOrUpdateConfigurationAsync(FullKey key, JObject value, string author);

    Task<Configuration> PatchConfigurationAsync(FullKey key, JArray patchOperations, string author);

    Task ClearConfigurationAsync(FullKey key);

    Task DeleteAsync(FullKey key);

    Task DeleteWithDescendantsAsync(FullKey key);

    Task<IReadOnlyCollection<ConfigurationVersion>> GetConfigurationVersionsAsync(FullKey key);

    Task<Configuration?> GetConfigurationVersionContentAsync(FullKey key, uint version);

    Task<IReadOnlyCollection<string>> GetConfigurationVersionChangesAsync(FullKey key, uint version);

    Task<IReadOnlyCollection<string>> GetConfigurationVersionChangesAsync(FullKey key, uint fromVersion, uint toVersion);

    Task<IReadOnlyCollection<string>> GetConfigurationViewChangesAsync(FullKey sourceKey, string toView);
}
