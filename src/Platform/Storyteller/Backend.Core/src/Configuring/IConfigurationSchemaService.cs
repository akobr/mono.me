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

    // Validates an own document against the combined schema of the key. With an overlay (the dry run of an import),
    // the layers the archive brings replace the stored ones.
    Task ValidateContentAsync(string organization, string project, string view, string annotationKey, JObject content, ViewImportOverlay? overlay = null);

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

    // The schemas of a view whose current document is not empty, by their canonical keys (lower-case type code,
    // AnnotationKey.ToString()). One query of the schema partition (the existence check before an import with Skip).
    Task<IReadOnlySet<SchemaKey>> GetSchemaKeysAsync(
        string organization,
        string project,
        string view,
        CancellationToken cancellationToken = default);

    // Replaces the schema with the given document as a new version, unless it is equal (an import). The schema of an
    // annotation that is not active (neither stored nor in the overlay) is skipped, and so is an empty document; a
    // document that is not a valid JSON Schema fails, even with Force. The configurations are checked as the import
    // leaves them (the overlay's documents and schema layers); a violation fails unless forced, and a forced write
    // carries the warning SchemaViolation.
    Task<ViewImportResult> ImportSchemaAsync(
        ViewSchemaTarget target,
        JObject content,
        ViewImportOptions options,
        ViewImportOverlay overlay);
}
