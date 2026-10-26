namespace _42.Platform.Storyteller.Mcp.Hosting;

// Section Mcp:Authentication: whose tokens the MCP endpoint accepts.
public sealed class McpAuthenticationOptions
{
    public const string DefaultScope = "Mcp.Access";

    // The authorization server MCP clients sign in with: the AuthKit domain or the Keycloak realm
    // (https://{host}/realms/{realm}); no trailing slash. Entra ID derives its issuers from TenantId, so it may stay empty.
    public string? Issuer { get; set; }

    // The aud of inbound tokens; empty means the resource URL, which is what clients send as RFC 8707 resource.
    public string? Audience { get; set; }

    // The scope (Entra ID, Keycloak) or permission (AuthKit) a token needs; Entra ID clients request {Audience}/{Scope}.
    public string Scope { get; set; } = DefaultScope;

    // The MCP client apps whose tokens the server accepts (azp, appid or client_id). Required for Entra ID; empty means
    // any client under AuthKit and Keycloak, where MCP clients register themselves.
    public List<string> AllowedClientIds { get; set; } = [];

    // Entra ID only: the tenant whose tokens are accepted.
    public string? TenantId { get; set; }

    // Entra ID only: the MCP app registration. Entra v2 tokens carry it as aud, so it is an accepted audience too.
    public string? ClientId { get; set; }
}
