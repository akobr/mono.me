using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

public interface IConfigurationService
{
    Task<bool> HasConfigurationContentAsync(FullKey key);

    Task<JObject?> GetConfigurationAsync(FullKey key);

    Task<JObject?> GetResolvedConfigurationAsync(FullKey key);

    Task<JObject> CreateOrUpdateConfigurationAsync(FullKey key, JObject value, string author);

    Task ClearConfigurationAsync(FullKey key);

    Task DeleteAsync(FullKey key);

    Task DeleteWithDescendantsAsync(FullKey key);

    Task<IReadOnlyCollection<ConfigurationVersion>> GetConfigurationVersionsAsync(FullKey key);

    Task<JObject?> GetConfigurationVersionContentAsync(FullKey key, uint version);

    Task<IReadOnlyCollection<string>> GetConfigurationVersionChangesAsync(FullKey key, uint version);
}
