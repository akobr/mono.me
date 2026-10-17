using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

public interface IConfigurationTemplateService
{
    Task<ConfigurationTemplate?> GetTemplateAsync(string organization, string project, string view, string annotationType);

    // The templates of the view without their content.
    Task<IReadOnlyList<ConfigurationTemplateSummary>> ListTemplatesAsync(string organization, string project, string view);

    Task<ConfigurationTemplate> CreateOrUpdateTemplateAsync(string organization, string project, string view, string annotationType, JObject value, AuditActor actor);

    Task<ConfigurationTemplate> PatchTemplateAsync(string organization, string project, string view, string annotationType, JArray patchOperations, AuditActor actor);

    Task<bool> DeleteTemplateAsync(string organization, string project, string view, string annotationType);

    Task<IReadOnlyCollection<ConfigurationVersion>> GetTemplateVersionsAsync(string organization, string project, string view, string annotationType);

    Task<ConfigurationTemplate?> GetTemplateVersionContentAsync(string organization, string project, string view, string annotationType, uint version);

    Task<DiffResult> GetTemplateVersionChangesAsync(string organization, string project, string view, string annotationType, uint version);

    Task<DiffResult> GetTemplateVersionChangesAsync(string organization, string project, string view, string annotationType, uint fromVersion, uint toVersion);

    // The templates of a view and their versions still in the history, one per type code, for an export. An empty
    // document is not a version; a deleted template has versions but no current document.
    Task<IReadOnlyList<TemplateHistory>> GetTemplateHistoriesAsync(
        string organization,
        string project,
        string view,
        CancellationToken cancellationToken = default);

    // Replaces the template of the type with the given document as a new version, unless it is equal (an import).
    // $remove and $patch apply to the document alone, as on a create. An empty document is skipped (a template is
    // cleared by a delete), and with Skip so is a different stored template. Templates have no schema, so Force does
    // not apply.
    Task<ViewImportResult> ImportTemplateAsync(
        string organization,
        string project,
        string view,
        string annotationType,
        JObject content,
        ViewImportOptions options);
}
