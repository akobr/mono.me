using System.Text;

namespace _42.Platform.Storyteller.Sdk.UnitTests;

internal static class TestTokens
{
    // An unsigned JWT with only exp; the SDK never validates, it only reads exp.
    public static string Jwt(DateTimeOffset expiresAt, string marker = "a")
    {
        static string Encode(string json)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        return $"{Encode("""{"alg":"RS256"}""")}.{Encode($$"""{"sub":"machine","exp":{{expiresAt.ToUnixTimeSeconds()}},"m":"{{marker}}"}""")}.sig";
    }

    public static string TokenResponse(string accessToken, long? expiresIn = 3600, string? tokenType = "Bearer")
    {
        var type = tokenType is null ? string.Empty : $", \"token_type\": \"{tokenType}\"";
        var lifetime = expiresIn is null ? string.Empty : $", \"expires_in\": {expiresIn}";
        return $"{{ \"access_token\": \"{accessToken}\"{type}{lifetime} }}";
    }
}
