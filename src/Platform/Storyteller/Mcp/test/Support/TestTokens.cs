using System.Security.Cryptography;

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// Signs tokens with a test key that the test host trusts instead of the provider's keys: Entra ID (Create), AuthKit
// (CreateAuthKit) and Keycloak (CreateKeycloak), each with the claims its provider writes.
internal static class TestTokens
{
    public const string TenantId = "8a7b6c5d-4e3f-4a1b-8c9d-0e1f2a3b4c5d";
    public const string OtherTenantId = "11111111-2222-4333-8444-555555555555";
    public const string McpClientId = "2b1a0c9d-8e7f-4a6b-9c5d-4e3f2a1b0c9d";
    public const string ClientAppId = "aebc6443-996d-45c2-90f0-388ff96faa56";
    public const string OtherClientAppId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46";
    public const string ResourceUrl = "https://mcp.test/mcp";
    public const string Scope = "Mcp.Access";

    public const string AuthKitIssuer = "https://auth.mcp.test";
    public const string AuthKitUserId = "user_01JMCPTESTUSER0000000000";
    public const string AuthKitClientId = "client_01JMCPTESTCLIENT00000000";
    public const string OtherAuthKitClientId = "client_01JOTHERCLIENT0000000000";

    public const string KeycloakIssuer = "https://keycloak.mcp.test/realms/storyteller";
    public const string KeycloakUserId = "0f3c2b1a-9d8e-4f7a-8b6c-5d4e3f2a1b0c";
    public const string KeycloakClientId = "mcp-client";
    public const string OtherKeycloakClientId = "other-client";

    private static readonly RSA Rsa = RSA.Create(2048);

    public static RsaSecurityKey Key { get; } = new(Rsa) { KeyId = "mcp-test-key" };

    public static string V2Issuer => $"https://login.microsoftonline.com/{TenantId}/v2.0";

    // The OpenID metadata of the issuer: its name and the test key.
    public static OpenIdConnectConfiguration CreateConfiguration(string? issuer = null)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = issuer ?? V2Issuer };
        configuration.SigningKeys.Add(Key);
        return configuration;
    }

    // A valid delegated Entra ID token of the allowed client app; overrides replace or remove (null) single claims.
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

        return Sign(claims, overrides, issuer ?? V2Issuer, audience ?? McpClientId, expires, signingKey);
    }

    // A valid AuthKit token of a WorkOS user for the MCP resource: the client in client_id, the scope as a permission.
    public static string CreateAuthKit(
        string subject = AuthKitUserId,
        IDictionary<string, object?>? overrides = null,
        string? issuer = null,
        string? audience = null,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["client_id"] = AuthKitClientId,
            ["sid"] = "session_01JMCPTESTSESSION000000000",
            ["permissions"] = new[] { Scope },
        };

        return Sign(claims, overrides, issuer ?? AuthKitIssuer, audience ?? ResourceUrl, expires, signingKey);
    }

    // A valid Keycloak access token of a person of the realm: typ Bearer, the client in azp, the scope in scope.
    public static string CreateKeycloak(
        string subject = KeycloakUserId,
        IDictionary<string, object?>? overrides = null,
        string? issuer = null,
        string? audience = null,
        DateTime? expires = null,
        SecurityKey? signingKey = null)
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["typ"] = "Bearer",
            ["azp"] = KeycloakClientId,
            ["scope"] = $"openid profile email {Scope}",
            ["preferred_username"] = "ada",
            ["name"] = "Ada Lovelace",
        };

        return Sign(claims, overrides, issuer ?? KeycloakIssuer, audience ?? ResourceUrl, expires, signingKey);
    }

    private static string Sign(
        Dictionary<string, object> claims,
        IDictionary<string, object?>? overrides,
        string issuer,
        string audience,
        DateTime? expires,
        SecurityKey? signingKey)
    {
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
            Issuer = issuer,
            Audience = audience,
            Claims = claims,
            NotBefore = expiresAt.AddHours(-1),
            IssuedAt = expiresAt.AddHours(-1),
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(signingKey ?? Key, SecurityAlgorithms.RsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
