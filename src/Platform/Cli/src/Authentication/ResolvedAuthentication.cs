using System.Collections.Generic;

namespace _42.Platform.Cli.Authentication;

// The sign-in settings in effect, from app.config.json, the discovery cache, or the server.
public sealed record ResolvedAuthentication
{
    public required AuthenticationProvider Provider { get; init; }

    public required string ClientId { get; init; }

    // Entra ID only.
    public string? TenantId { get; init; }

    // Entra ID only. Empty uses the default sform scopes.
    public IReadOnlyList<string> Scopes { get; init; } = [];

    // AuthKit only.
    public string AuthKitApiBaseUrl { get; init; } = AuthKitDefaults.ApiBaseUrl;
}
