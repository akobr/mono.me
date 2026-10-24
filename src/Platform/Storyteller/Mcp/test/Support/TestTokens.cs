using System.Security.Cryptography;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// Signs tokens with a test key that the test host trusts instead of Entra's keys.
internal static class TestTokens
{
    public const string TenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d";
    public const string OtherTenantId = "11111111-2222-4333-8444-555555555555";
    public const string McpClientId = "2b1a0c9d-8e7f-4a6b-9c5d-4e3f2a1b0c9d";
    public const string ClientAppId = "aebc6443-996d-45c2-90f0-388ff96faa56";
    public const string OtherClientAppId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46";
    public const string ResourceUrl = "https://mcp.test/mcp";
    public const string Scope = "Mcp.Access";

    private static readonly RSA Rsa = RSA.Create(2048);

    public static RsaSecurityKey Key { get; } = new(Rsa) { KeyId = "mcp-test-key" };

    public static string V2Issuer => $"https://login.microsoftonline.com/{TenantId}/v2.0";

    public static OpenIdConnectConfiguration CreateConfiguration()
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = V2Issuer };
        configuration.SigningKeys.Add(Key);
        return configuration;
    }

    // A valid delegated token of the allowed client app; overrides replace or remove (null) single claims.
    public static string Create(
        string subject = "user-1",
        IDictionary<string, object?>? overrides = null,
        string? issuer = null,
        string? audience = null,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["oid"] = $"oid-{subject}",
            ["tid"] = TenantId,
            ["azp"] = ClientAppId,
            ["scp"] = Scope,
            ["name"] = $"Name of {subject}",
            ["ver"] = "2.0",
        };

        foreach (var (name, value) in overrides ?? new Dictionary<string, object?>())
        {
            if (value is null)
            {
                claims.Remove(name);
            }
            else
            {
                claims[name] = value;
            }
        }

        var expiresAt = expires ?? DateTime.UtcNow.AddMinutes(30);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer ?? V2Issuer,
            Audience = audience ?? McpClientId,
            Claims = claims,
            NotBefore = expiresAt.AddHours(-1),
            IssuedAt = expiresAt.AddHours(-1),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(signingKey ?? Key, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
