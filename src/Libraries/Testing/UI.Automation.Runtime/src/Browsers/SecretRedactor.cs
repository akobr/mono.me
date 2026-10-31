using System.Net;
using System.Text.Json;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// Replaces secrets by *** in text that is logged or thrown, in every form a URL or a JSON document may carry them:
// as is, URL-encoded (%20 and + for a space), JSON-escaped and both together. Playwright 1.63 and 1.64 leave the query
// out of the endpoint in their call log, but an error of the grid, or of another Playwright version, may still echo the
// caps URL.
internal sealed class SecretRedactor
{
    private readonly string[] forms;

    public SecretRedactor(params string?[] secrets)
    {
        forms = secrets
            .Where(secret => !string.IsNullOrEmpty(secret))
            .SelectMany(secret => Forms(secret!))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(form => form.Length)
            .ToArray();
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        foreach (var form in forms)
        {
            text = text.Replace(form, SettingsRedaction.Mask, StringComparison.Ordinal);
        }

        return text;
    }

    private static IEnumerable<string> Forms(string secret)
    {
        var json = JsonEncodedText.Encode(secret).ToString();
        foreach (var value in new[] { secret, json })
        {
            yield return value;
            yield return Uri.EscapeDataString(value);
            yield return WebUtility.UrlEncode(value);
        }
    }
}
