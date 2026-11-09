using System;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _42.Platform.Cli.Authentication;

// What the user needs to finish a device sign-in in a browser.
public sealed record DeviceCodePrompt(string UserCode, Uri VerificationUri, Uri? VerificationUriComplete, TimeSpan ExpiresIn)
{
    private static readonly JsonSerializerOptions LineJson = new()
    {
        WriteIndented = false,

        // An address keeps its & and + as they are.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // The contract of SFORM_DEVICE_CODE_OUTPUT=json, one line:
    // {"userCode":"…","verificationUri":"…","verificationUriComplete":"…" or null,"expiresIn":<whole seconds>}.
    public string ToJsonLine()
    {
        return JsonSerializer.Serialize(
            new JsonPrompt(
                UserCode,
                VerificationUri.AbsoluteUri,
                VerificationUriComplete?.AbsoluteUri,
                (int)Math.Max(0, Math.Round(ExpiresIn.TotalSeconds))),
            LineJson);
    }

    private sealed record JsonPrompt(
        [property: JsonPropertyName("userCode")] string UserCode,
        [property: JsonPropertyName("verificationUri")] string VerificationUri,
        [property: JsonPropertyName("verificationUriComplete")] string? VerificationUriComplete,
        [property: JsonPropertyName("expiresIn")] int ExpiresIn);
}
