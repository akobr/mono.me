using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// Authorization server metadata (RFC 8414) as the MCP host serves it for Entra ID.
public sealed class AuthorizationServerDocument
{
    [JsonPropertyName("issuer")]
    public required string Issuer { get; init; }

    [JsonPropertyName("authorization_endpoint")]
    public required string AuthorizationEndpoint { get; init; }

    [JsonPropertyName("token_endpoint")]
    public required string TokenEndpoint { get; init; }

    [JsonPropertyName("response_types_supported")]
    public IReadOnlyList<string> ResponseTypesSupported { get; init; } = ["code"];

    [JsonPropertyName("grant_types_supported")]
    public IReadOnlyList<string> GrantTypesSupported { get; init; } = ["authorization_code", "refresh_token"];

    [JsonPropertyName("code_challenge_methods_supported")]
    public IReadOnlyList<string> CodeChallengeMethodsSupported { get; init; } = ["S256"];

    // Public clients with PKCE: MCP clients are pre-registered public client apps of the tenant.
    [JsonPropertyName("token_endpoint_auth_methods_supported")]
    public IReadOnlyList<string> TokenEndpointAuthMethodsSupported { get; init; } = ["none"];

    [JsonPropertyName("scopes_supported")]
    public IReadOnlyList<string> ScopesSupported { get; init; } = [];
}
