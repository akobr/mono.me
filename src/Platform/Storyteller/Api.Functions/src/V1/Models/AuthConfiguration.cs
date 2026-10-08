namespace _42.Platform.Storyteller.Api.V1.Models;

// Public sign-in settings for clients such as sform. Never carries secrets.
public record class AuthConfiguration
{
    // EntraId or AuthKit.
    public required string Provider { get; init; }

    // Entra API application ID, or the AuthKit client_… ID.
    public required string ClientId { get; init; }

    // Entra ID only.
    public string? TenantId { get; init; }

    // Scopes a client requests at sign-in. Entra ID only; AuthKit device sign-in takes no scopes.
    public IReadOnlyList<string>? Scopes { get; init; }

    // AuthKit only, when configured.
    public string? AuthKitDomain { get; init; }
}
