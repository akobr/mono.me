using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Configuring;

public interface IConfigurationSchemaService
{
    Task<ConfigurationSchema?> GetSchemaAsync(string organization, string project, string annotationType);

    Task<ConfigurationSchema> SetSchemaAsync(string organization, string project, string annotationType, JObject schemaContent, string author);

    Task<bool> DeleteSchemaAsync(string organization, string project, string annotationType);
}
