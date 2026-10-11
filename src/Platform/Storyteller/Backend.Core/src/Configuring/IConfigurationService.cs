using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

public interface IConfigurationService
{
    Task<bool> HasConfigurationContentAsync(FullKey key);

    Task<Configuration?> GetRawConfigurationAsync(FullKey key);

    // Configurations of a view without their documents, up to 1000 per page.
    // annotationType is a type code (rst, sbt, …); keyPrefix filters the annotation keys.
    Task<ConfigurationsResponse> ListConfigurationsAsync(
        string organization,
        string project,
        string view,
        string? annotationType = null,
        string? keyPrefix = null,
        string? continuationToken = null);

    Task<Configuration?> GetResolvedConfigurationAsync(FullKey key, bool includeSecrets = false);

    Task<Configuration?> GetResolvedConfigurationWithSecretsAsync(FullKey key);

    Task<Configuration?> GetResolvedConfigurationWithoutSecretsAsync(FullKey key);

    Task<Configuration?> GetConfigurationHierarchyViewAsync(FullKey key);

    // With an actor, the change is also recorded in the annotation's audit (best effort, after the write).
    Task<Configuration> CreateOrUpdateConfigurationAsync(FullKey key, JObject value, string author, bool force = false, AuditActor? actor = null);

    Task<Configuration> PatchConfigurationAsync(FullKey key, JArray patchOperations, string author, bool force = false, AuditActor? actor = null);

    Task ClearConfigurationAsync(FullKey key, AuditActor? actor = null);

    Task DeleteAsync(FullKey key, AuditActor? actor = null);

    Task<IReadOnlyCollection<ConfigurationVersion>> GetConfigurationVersionsAsync(FullKey key);

    Task<Configuration?> GetConfigurationVersionContentAsync(FullKey key, uint version);

    Task<DiffResult> GetConfigurationVersionChangesAsync(FullKey key, uint version);

    Task<DiffResult> GetConfigurationVersionChangesAsync(FullKey key, uint fromVersion, uint toVersion);

    Task<DiffResult> GetConfigurationViewChangesAsync(FullKey sourceKey, string toView);
}
