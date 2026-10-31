using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace _42.Testing.UI.Automation.Runtime.Settings;

// Keeps secrets out of logs, run descriptions and reports: a key is a secret when a segment of it contains Password,
// AccessKey, ApiKey, Secret or Token (case-insensitive), e.g. Users:Pool:0:Password or Reporting:Testiny:ApiKey.
public static class SettingsRedaction
{
    public const string Mask = "***";

    private static readonly string[] SecretWords = ["Password", "AccessKey", "ApiKey", "Secret", "Token"];

    private static readonly JsonSerializerOptions FlattenOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static bool IsSecret(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        foreach (var segment in key.Split(ConfigurationPath.KeyDelimiter))
        {
            foreach (var word in SecretWords)
            {
                if (segment.Contains(word, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // *** for a secret with a value; an empty secret stays empty, so a missing password shows.
    public static string? Redact(string key, string? value)
    {
        return IsSecret(key) && !string.IsNullOrEmpty(value) ? Mask : value;
    }

    // Every key of the configuration and every effective value of the six sections (defaults included), sorted.
    internal static string Describe(UiAutomationSettings settings)
    {
        var values = new SortedDictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in settings.Configuration.AsEnumerable())
        {
            if (value is not null)
            {
                values[key] = value;
            }
        }

        Flatten(values, "Target", settings.Target);
        Flatten(values, "Browser", settings.Browser);
        Flatten(values, "Execution", settings.Execution);
        Flatten(values, "Users", settings.Users);
        Flatten(values, "BrowserStack", settings.BrowserStack);
        Flatten(values, "Reporting", settings.Reporting);

        return string.Join(Environment.NewLine, values.Select(pair => $"{pair.Key} = {Redact(pair.Key, pair.Value)}"));
    }

    private static void Flatten(IDictionary<string, string?> values, string section, object value)
    {
        Flatten(values, section, JsonSerializer.SerializeToElement(value, value.GetType(), FlattenOptions));
    }

    private static void Flatten(IDictionary<string, string?> values, string key, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(values, ConfigurationPath.Combine(key, property.Name), property.Value);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(values, ConfigurationPath.Combine(key, index.ToString(System.Globalization.CultureInfo.InvariantCulture)), item);
                    index++;
                }

                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                break;

            case JsonValueKind.String:
                values[key] = element.GetString();
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                values[key] = element.ValueKind == JsonValueKind.True ? "true" : "false";
                break;

            default:
                values[key] = element.GetRawText();
                break;
        }
    }
}
