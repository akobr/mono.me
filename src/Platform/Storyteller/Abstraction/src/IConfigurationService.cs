using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller;

public interface IConfigurationService
{
    Task<JObject?> GetConfigurationAsync(FullKey key);

    Task<JObject> CreateOrUpdateConfigurationAsync(FullKey key, JObject value);

    Task<bool> DeleteConfigurationAsync(FullKey key);
}
