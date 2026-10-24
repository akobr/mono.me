namespace _42.Platform.Storyteller.Mcp.Hosting;

public sealed class McpEntraIdOptions
{
    public const string DefaultScope = "Mcp.Access";

    public string TenantId { get; set; } = string.Empty;

    // The MCP app registration: the audience of inbound tokens and the confidential client of on-behalf-of.
    public string ClientId { get; set; } = string.Empty;

    // The App ID URI of the MCP app; empty means the resource URL, which is what Entra accepts as RFC 8707 resource.
    public string? Audience { get; set; }

    public string Scope { get; set; } = DefaultScope;

    // The MCP client apps whose tokens the server accepts (azp, or appid in v1 tokens).
    public List<string> AllowedClientIds { get; set; } = [];

    public string? ClientSecret { get; set; }

    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    public bool UseManagedIdentity { get; set; }

    // A user-assigned managed identity; empty with UseManagedIdentity means the system-assigned one.
    public string? ManagedIdentityClientId { get; set; }
}
