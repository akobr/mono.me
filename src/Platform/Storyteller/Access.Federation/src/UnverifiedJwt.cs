using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace _42.Platform.Storyteller;

// A JWS compact token read before its signature is checked: only for limits and for picking the trusted issuer.
// Nothing read here is trusted; the payload is matched against rules only after the signature passed.
internal sealed class UnverifiedJwt
{
    // Two claims with one name would let this reader and the token handler see different values.
    private static readonly JsonDocumentOptions ParseOptions = new() { AllowDuplicateProperties = false, MaxDepth = 32 };

    private UnverifiedJwt(string issuer, string? keyId, string? algorithm, JsonElement payload)
    {
        Issuer = issuer;
        KeyId = keyId;
        Algorithm = algorithm;
        Payload = payload;
    }

    // The `iss` claim, a non-empty string.
    public string Issuer { get; }

    // The `kid` header, null when missing or not a string.
    public string? KeyId { get; }

    public string? Algorithm { get; }

    // The decoded payload, a JSON object.
    public JsonElement Payload { get; }

    // False with the reason (for the logs) when the token is too large, is not in JWS compact form
    // (three base64url parts; a JWE has five) or has no string `iss`.
    public static bool TryRead(string? token, int maxBytes, [NotNullWhen(true)] out UnverifiedJwt? jwt, out string reason)
    {
        jwt = null;

        if (string.IsNullOrEmpty(token))
        {
            reason = "the token is empty";
            return false;
        }

        // A UTF-8 byte per character at least, so a long string is refused before it is measured.
        if (token.Length > maxBytes || Encoding.UTF8.GetByteCount(token) > maxBytes)
        {
            reason = $"the token has more than {maxBytes} bytes";
            return false;
        }

        var parts = token.Split('.');

        if (parts.Length != 3 || parts.Any(part => part.Length < 1 || !Base64Url.IsValid(part)))
        {
            reason = "the token is not in JWS compact form";
            return false;
        }

        if (!TryParseObject(parts[0], out var header) || !TryParseObject(parts[1], out var payload))
        {
            reason = "the token header or payload is not a JSON object";
            return false;
        }

        if (!payload.TryGetProperty("iss", out var issuer)
            || issuer.ValueKind != JsonValueKind.String
            || issuer.GetString() is not { Length: > 0 } issuerValue)
        {
            reason = "the token has no issuer";
            return false;
        }

        jwt = new UnverifiedJwt(issuerValue, GetString(header, "kid"), GetString(header, "alg"), payload);
        reason = string.Empty;
        return true;
    }

    private static bool TryParseObject(string part, out JsonElement element)
    {
        element = default;

        try
        {
            using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(part), ParseOptions);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            element = document.RootElement.Clone();
            return true;
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            return false;
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }
}
