using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Json;

public interface IJsonSerializationSettingsProvider
{
    JsonSerializerSettings GetSettings(string? settingsName = null);
}
