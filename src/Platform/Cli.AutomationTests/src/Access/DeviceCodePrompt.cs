using System.Text.Json;
using System.Text.Json.Serialization;

namespace _42.Platform.Cli.AutomationTests.Access;

// The device code of a sign-in as sform writes it for programs (SFORM_DEVICE_CODE_OUTPUT=json): one JSON line, the first
// thing on the standard error, {"userCode":…,"verificationUri":…,"verificationUriComplete":…,"expiresIn":<seconds>}.
// verificationUriComplete is null when the provider has none (Entra ID); expiresIn is whole seconds, never negative.
public sealed record DeviceCodePrompt(
    [property: JsonPropertyName("userCode")] string UserCode,
    [property: JsonPropertyName("verificationUri")] Uri VerificationUri,
    [property: JsonPropertyName("verificationUriComplete")] Uri? VerificationUriComplete,
    [property: JsonPropertyName("expiresIn")] int ExpiresIn)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // The address to open: the complete one (the code filled in), else the one where the code is typed.
    public Uri Address => VerificationUriComplete ?? VerificationUri;

    // Whether the line is the JSON of a device code (a JSON object with userCode and verificationUri).
    public static bool IsPrompt(string? line)
    {
        return TryParse(line, out _);
    }

    public static bool TryParse(string? line, out DeviceCodePrompt prompt)
    {
        prompt = null!;
        var text = line?.Trim();
        if (string.IsNullOrEmpty(text) || text[0] != '{')
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<DeviceCodePrompt>(text, Options);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.UserCode) || parsed.VerificationUri is not { IsAbsoluteUri: true })
            {
                return false;
            }

            prompt = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The code from sform's text prompt on the standard output (without SFORM_DEVICE_CODE_OUTPUT): "To sign in, open <uri>
    // and enter the code <code>.", "Or open <complete uri> with the code filled in." and "The code expires in N minute(s).".
    public static bool TryParseText(string? plainOutput, out DeviceCodePrompt prompt)
    {
        prompt = null!;
        if (string.IsNullOrEmpty(plainOutput))
        {
            return false;
        }

        var open = System.Text.RegularExpressions.Regex.Match(plainOutput, @"To sign in, open (?<uri>\S+) and enter the code (?<code>\S+?)\.(\s|$)");
        if (!open.Success || !Uri.TryCreate(open.Groups["uri"].Value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var complete = System.Text.RegularExpressions.Regex.Match(plainOutput, @"Or open (?<uri>\S+) with the code filled in");
        var expires = System.Text.RegularExpressions.Regex.Match(plainOutput, @"The code expires in (?<minutes>\d+) minute");
        prompt = new DeviceCodePrompt(
            open.Groups["code"].Value,
            uri,
            complete.Success && Uri.TryCreate(complete.Groups["uri"].Value, UriKind.Absolute, out var completeUri) ? completeUri : null,
            expires.Success ? int.Parse(expires.Groups["minutes"].Value, System.Globalization.CultureInfo.InvariantCulture) * 60 : 0);
        return true;
    }

    // Throws a FormatException that quotes the line when it is not a device code.
    public static DeviceCodePrompt Parse(string line)
    {
        return TryParse(line, out var prompt)
            ? prompt
            : throw new FormatException($"Not the JSON line of a device code: '{line}'.");
    }
}
