namespace _42.Platform.Storyteller.Api.V1.Models;

// Public sign-in settings for clients such as sform. Never carries secrets.
public record class AuthConfiguration
{
    // EntraId, AuthKit or Keycloak.
    public required string Provider { get; init; }

    // Entra API application ID, the AuthKit client_… ID, or the Keycloak client to sign in with
    // (empty when Auth:Keycloak:ClientId is not set).
    public required string ClientId { get; init; }

    // Entra ID only.
    public string? TenantId { get; init; }

    // Scopes a client requests at sign-in. Entra ID only; AuthKit device sign-in takes no scopes.
    public IReadOnlyList<string>? Scopes { get; init; }

    // AuthKit only, when configured.
    public string? AuthKitDomain { get; init; }

    // Keycloak only: the realm issuer, whose OpenID metadata names the sign-in endpoints.
    public string? Issuer { get; init; }
}
