using System;
using System.Text.Json;

namespace _42.Platform.Cli.Authentication;

internal static class JwtExpiry
{
    // Unverified read of exp; the API validates the token, sform only needs to know when to refresh.
    public static DateTimeOffset? Read(string token)
    {
        var parts = token.Split('.');

        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            using var payload = JsonDocument.Parse(DecodeBase64Url(parts[1]));
            return payload.RootElement.ValueKind == JsonValueKind.Object
                && payload.RootElement.TryGetProperty("exp", out var exp)
                && exp.TryGetInt64(out var seconds)
                ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                : null;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '='));
    }
}
