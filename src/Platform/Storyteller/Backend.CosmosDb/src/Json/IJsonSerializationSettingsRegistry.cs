using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Json;

public interface IJsonSerializationSettingsRegistry
{
    void RegisterSettings(JsonSerializerSettings settings, string? settingsName = null);
}
