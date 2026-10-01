using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

public interface IConfigurationSchemaService
{
    Task<ConfigurationSchema?> GetSchemaAsync(string organization, string project, string view, string annotationType);

    Task<ConfigurationSchema> SetSchemaAsync(string organization, string project, string view, string annotationType, JObject schemaContent, string author, bool force);

    Task<bool> DeleteSchemaAsync(string organization, string project, string view, string annotationType);

    Task<ConfigurationSchema?> GetAnnotationSchemaAsync(string organization, string project, string view, string annotationKey);

    Task<ConfigurationSchema> SetAnnotationSchemaAsync(string organization, string project, string view, string annotationKey, JObject schemaContent, string author, bool force);

    Task<bool> DeleteAnnotationSchemaAsync(string organization, string project, string view, string annotationKey);

    Task<ConfigurationSchema?> GetDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode);

    Task<ConfigurationSchema> SetDescendantTypeSchemaAsync(string organization, string project, string view, string annotationKey, string descendantTypeCode, JObject schemaContent, string author, bool force);

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
}
