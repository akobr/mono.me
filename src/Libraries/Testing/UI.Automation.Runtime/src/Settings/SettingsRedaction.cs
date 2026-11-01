using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Testing.UI.Automation.Runtime.Browsers;
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

    // The values of the settings' secrets: of every configuration key IsSecret finds (Users:Pool:<n>:Password,
    // BrowserStack:AccessKey, Reporting:Testiny:ApiKey, a consumer's own), the bound pool passwords and access key, and
    // the password in the user info of Target:BaseUrl (https://user:password@host/), as written and URL-decoded.
    public static IReadOnlyList<string> SecretValues(UiAutomationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var values = new List<string>();
        foreach (var (key, value) in settings.Configuration.AsEnumerable())
        {
            if (!string.IsNullOrEmpty(value) && IsSecret(key))
            {
                values.Add(value);
            }
        }

        values.AddRange(settings.Users.Pool.Select(user => user.Password).OfType<string>());
        if (settings.BrowserStack.AccessKey is { } accessKey)
        {
            values.Add(accessKey);
        }

        if (Uri.TryCreate(settings.Target.BaseUrl?.Trim(), UriKind.Absolute, out var target) && target.UserInfo.IndexOf(':', StringComparison.Ordinal) is var colon and >= 0)
        {
            var password = target.UserInfo[(colon + 1)..];
            values.Add(password);
            values.Add(Uri.UnescapeDataString(password));
        }

        return values.Where(value => value.Trim().Length > 0).Distinct(StringComparer.Ordinal).ToArray();
    }

    // Replaces every secret value of the settings (SecretValues) by ***, as is, trimmed, URL-encoded and JSON-escaped;
    // with includeEmails also the pool users' e-mail addresses, which no report shows. For text that leaves the process:
    // failure messages and stack traces, logs. Built from the settings as they are now.
    public static Func<string?, string> CreateRedactor(UiAutomationSettings settings, bool includeEmails = false)
    {
        ArgumentNullException.ThrowIfNull(settings);

        IEnumerable<string?> values = SecretValues(settings);
        if (includeEmails)
        {
            values = values.Concat(settings.Users.Pool.Select(user => user.Email));
        }

        return new SecretRedactor(values).Redact;
    }

    // The secret keys of the configuration whose value is too short to be redacted (SecretRedactor.MinimumLength).
    internal static IEnumerable<string> ShortSecretKeys(UiAutomationSettings settings)
    {
        return settings.Configuration.AsEnumerable()
            .Where(pair => IsSecret(pair.Key) && pair.Value?.Trim().Length is > 0 and < SecretRedactor.MinimumLength)
            .Select(pair => pair.Key)
            .Order(StringComparer.OrdinalIgnoreCase);
    }

    // Every key of the configuration and every effective value of the six sections (defaults included), sorted; a
    // secret key shows ***, and a secret value elsewhere (the password in Target:BaseUrl) too.
    internal static string Describe(UiAutomationSettings settings)
    {
        var redactor = new SecretRedactor(SecretValues(settings));
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

        return string.Join(Environment.NewLine, values.Select(pair => $"{pair.Key} = {redactor.Redact(Redact(pair.Key, pair.Value))}"));
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
