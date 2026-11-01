using System.Net;
using System.Text.Json;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.Browsers;

// Replaces secrets by *** in text that is logged or thrown, in every form a URL or a JSON document may carry them:
// as is, URL-encoded (%20 and + for a space), JSON-escaped and both together, each also trimmed (a value pasted with a
// trailing newline still authenticates, because the capabilities send it trimmed). Playwright 1.63 and 1.64 leave the
// query out of the endpoint in their call log, but an error of the grid, or of another Playwright version, may still
// echo the caps URL; and Playwright's call log of fill and type keeps the typed value, e.g. a password. A secret shorter
// than MinimumLength is left alone, as replacing it would mask ordinary text everywhere (the settings warn about it).
internal sealed class SecretRedactor
{
    public const int MinimumLength = 4;

    private readonly string[] forms;

    public SecretRedactor(params string?[] secrets)
        : this((IEnumerable<string?>)secrets)
    {
    }

    public SecretRedactor(IEnumerable<string?> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);

        forms = secrets
            .Where(secret => !string.IsNullOrEmpty(secret))
            .SelectMany(secret => new[] { secret!, secret!.Trim() })
            .Where(secret => secret.Trim().Length >= MinimumLength)
            .SelectMany(Forms)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(form => form.Length)
            .ToArray();
    }

    public string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text) || forms.Length == 0)
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
