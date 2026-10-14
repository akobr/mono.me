using _42.Platform.Storyteller.Transferring;
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

    // The actor stamps the new version and is recorded in the annotation's audit (best effort, after the write).
    Task<Configuration> CreateOrUpdateConfigurationAsync(FullKey key, JObject value, AuditActor actor, bool force = false);

    Task<Configuration> PatchConfigurationAsync(FullKey key, JArray patchOperations, AuditActor actor, bool force = false);

    Task ClearConfigurationAsync(FullKey key, AuditActor? actor = null);

    Task DeleteAsync(FullKey key, AuditActor? actor = null);

    Task<IReadOnlyCollection<ConfigurationVersion>> GetConfigurationVersionsAsync(FullKey key);

    Task<Configuration?> GetConfigurationVersionContentAsync(FullKey key, uint version);

    Task<DiffResult> GetConfigurationVersionChangesAsync(FullKey key, uint version);

    Task<DiffResult> GetConfigurationVersionChangesAsync(FullKey key, uint fromVersion, uint toVersion);

    Task<DiffResult> GetConfigurationViewChangesAsync(FullKey sourceKey, string toView);

    // The own documents of every configuration in a view and their versions still in the history, for an export.
    // Configurations of soft-deleted annotations are left out; an empty document is not a version.
    Task<IReadOnlyList<ConfigurationHistory>> GetConfigurationHistoriesAsync(
        string organization,
        string project,
        string view,
        CancellationToken cancellationToken = default);

    // Replaces the annotation's own document with the given one as a new version, unless it is equal (an import).
    // A missing or deleted annotation and an empty document are skipped; a schema violation fails unless forced.
    Task<ConfigurationImportResult> ImportConfigurationAsync(
        FullKey key,
        JObject content,
        AuditActor actor,
        bool force = false,
        bool dryRun = false);
}
