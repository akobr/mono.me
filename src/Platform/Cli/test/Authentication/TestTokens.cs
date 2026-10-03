using System;
using System.Text;

namespace _42.Platform.Cli.UnitTests.Authentication;

internal static class TestTokens
{
    // An unsigned JWT with only exp; the CLI never validates, it only reads exp.
    public static string AccessToken(DateTimeOffset expiresAt, string marker = "a")
    {
        static string Encode(string json)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        return $"{Encode("""{"alg":"RS256"}""")}.{Encode($$"""{"sub":"user_01","exp":{{expiresAt.ToUnixTimeSeconds()}},"m":"{{marker}}"}""")}.sig";
    }

    public static string SessionJson(string accessToken, string refreshToken, string? organizationId = "org_01")
    {
        var organization = organizationId is null ? "null" : $"\"{organizationId}\"";
        return $$"""
            {
              "user": { "object": "user", "id": "user_01", "email": "ada@example.com", "first_name": "Ada", "last_name": "Lovelace" },
              "organization_id": {{organization}},
              "access_token": "{{accessToken}}",
              "refresh_token": "{{refreshToken}}",
              "authentication_method": "GoogleOAuth"
            }
            """;
    }
}
