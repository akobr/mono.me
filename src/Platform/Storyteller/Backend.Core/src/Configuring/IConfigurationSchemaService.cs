using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

public interface IConfigurationSchemaService
{
    Task<ConfigurationSchema?> GetSchemaAsync(string organization, string project, string view, string annotationType);

    // Every schema document of the view (type, annotation, descendant type) without its content.
    Task<IReadOnlyList<ConfigurationSchemaSummary>> ListSchemasAsync(string organization, string project, string view);

    Task<ConfigurationSchema> SetSchemaAsync(string organization, string project, string view, string annotationType, JObject schemaContent, AuditActor actor, bool force);

    Task<bool> DeleteSchemaAsync(string organization, string project, string view, string annotationType);

    Task<ConfigurationSchema?> GetAnnotationSchemaAsync(string organization, string project, string view, string annotationKey);

    Task<ConfigurationSchema> SetAnnotationSchemaAsync(string organization, string project, string view, string annotationKey, JObject schemaContent, AuditActor actor, bool force);

    Task<bool> DeleteAnnotationSchemaAsync(string organization, string project, string view, string annotationKey);

    Task<ConfigurationSchema?> GetDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode);

    Task<ConfigurationSchema> SetDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode, JObject schemaContent, AuditActor actor, bool force);

    Task<bool> DeleteDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode);

    Task<CombinedConfigurationSchema?> GetCombinedSchemaAsync(string organization, string project, string view, string annotationKey);

    Task ValidateContentAsync(string organization, string project, string view, string annotationKey, JObject content);

    Task<IReadOnlyCollection<ConfigurationVersion>> GetSchemaVersionsAsync(string organization, string project, string view, string annotationType);

    Task<ConfigurationSchema?> GetSchemaVersionContentAsync(string organization, string project, string view, string annotationType, uint version);

    Task<DiffResult> GetSchemaVersionChangesAsync(string organization, string project, string view, string annotationType, uint version);

    Task<DiffResult> GetSchemaVersionChangesAsync(string organization, string project, string view, string annotationType, uint fromVersion, uint toVersion);

    Task<IReadOnlyCollection<ConfigurationVersion>> GetAnnotationSchemaVersionsAsync(string organization, string project, string view, string annotationKey);

    Task<ConfigurationSchema?> GetAnnotationSchemaVersionContentAsync(string organization, string project, string view, string annotationKey, uint version);

    Task<DiffResult> GetAnnotationSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, uint version);

    Task<DiffResult> GetAnnotationSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, uint fromVersion, uint toVersion);

    Task<IReadOnlyCollection<ConfigurationVersion>> GetDescendantTypeSchemaVersionsAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode);

    Task<ConfigurationSchema?> GetDescendantTypeSchemaVersionContentAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode, uint version);

    Task<DiffResult> GetDescendantTypeSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode, uint version);

    Task<DiffResult> GetDescendantTypeSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode, uint fromVersion, uint toVersion);

    // The schemas of a view and their versions still in the history, one per document (kind, type, key), for an
    // export. The annotation and descendant-type schemas of an annotation that is deleted or missing are left out. An
    // empty document is not a version; a deleted schema has versions but no current document.
    Task<IReadOnlyList<SchemaHistory>> GetSchemaHistoriesAsync(
        string organization,
        string project,
        string view,
        CancellationToken cancellationToken = default);
}
