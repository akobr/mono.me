using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

public interface IConfigurationSchemaService
{
    // Type-level CRUD (existing)
    Task<ConfigurationSchema?> GetSchemaAsync(string organization, string project, string annotationType);

    Task<ConfigurationSchema> SetSchemaAsync(string organization, string project, string annotationType, JObject schemaContent, string author);

    Task<bool> DeleteSchemaAsync(string organization, string project, string annotationType);

    // Annotation-level CRUD
    Task<ConfigurationSchema?> GetAnnotationSchemaAsync(string organization, string project, string annotationKey);

    Task<ConfigurationSchema> SetAnnotationSchemaAsync(string organization, string project, string annotationKey, JObject schemaContent, string author);

    Task<bool> DeleteAnnotationSchemaAsync(string organization, string project, string annotationKey);

    // Descendant-type CRUD
    Task<ConfigurationSchema?> GetDescendantTypeSchemaAsync(string organization, string project, string annotationKey, string descendantTypeCode);

    Task<ConfigurationSchema> SetDescendantTypeSchemaAsync(string organization, string project, string annotationKey, string descendantTypeCode, JObject schemaContent, string author);

    Task<bool> DeleteDescendantTypeSchemaAsync(string organization, string project, string annotationKey, string descendantTypeCode);

    // Combined schema
    Task<CombinedConfigurationSchema?> GetCombinedSchemaAsync(string organization, string project, string annotationKey);

    // Validate content against combined schema
    Task ValidateContentAsync(string organization, string project, string annotationKey, JObject content);
}
