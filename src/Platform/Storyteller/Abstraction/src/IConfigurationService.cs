using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller;

public interface IConfigurationService
{
    Task<JsonObject?> GetConfigurationAsync(string fullKey);

    Task<JsonObject> UpdateConfigurationAsync(string fullKey, JsonObject value);

    Task<bool> DeleteConfigurationAsync(string fullKey);
}
