using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

public interface IConfigurationTemplateService
{
    Task<ConfigurationTemplate?> GetTemplateAsync(string organization, string project, string view, string annotationType);

    Task<ConfigurationTemplate> CreateOrUpdateTemplateAsync(string organization, string project, string view, string annotationType, JObject value, string author);

    Task<ConfigurationTemplate> PatchTemplateAsync(string organization, string project, string view, string annotationType, JArray patchOperations, string author);

    Task<bool> DeleteTemplateAsync(string organization, string project, string view, string annotationType);

    Task<IReadOnlyCollection<ConfigurationVersion>> GetTemplateVersionsAsync(string organization, string project, string view, string annotationType);

    Task<ConfigurationTemplate?> GetTemplateVersionContentAsync(string organization, string project, string view, string annotationType, uint version);

    Task<DiffResult> GetTemplateVersionChangesAsync(string organization, string project, string view, string annotationType, uint version);

    Task<DiffResult> GetTemplateVersionChangesAsync(string organization, string project, string view, string annotationType, uint fromVersion, uint toVersion);
}
