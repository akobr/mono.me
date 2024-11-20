using System.Collections.Generic;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Json;

public class JsonSerializationSettingsService : IJsonSerializationSettingsProvider, IJsonSerializationSettingsRegistry
{
    private readonly JsonSerializerSettings _defaultSettings;
    private readonly Dictionary<string, JsonSerializerSettings> _settingsMap = new();

    public JsonSerializationSettingsService(IOptions<JsonSerializerSettings> serializerOptions)
    {
        _defaultSettings = serializerOptions.Value;
        _settingsMap[JsonSettingNames.Unique] = new JsonSerializerSettings(_defaultSettings)
        {
            ContractResolver = new OrderedContractResolver(),
        };
    }

    public JsonSerializerSettings GetSettings(string? settingsName = null)
    {
        if (string.IsNullOrEmpty(settingsName))
        {
            return _defaultSettings;
        }

        _settingsMap.TryGetValue(settingsName, out var settings);
        return settings ?? _defaultSettings;
    }

    public void RegisterSettings(JsonSerializerSettings settings, string? settingsName = null)
    {
        if (string.IsNullOrWhiteSpace(settingsName))
        {
            return;
        }

        _settingsMap[settingsName] = settings;
    }
}
