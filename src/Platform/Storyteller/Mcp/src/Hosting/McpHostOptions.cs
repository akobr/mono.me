namespace _42.Platform.Storyteller.Mcp.Hosting;

// Section Mcp: how the MCP server signs people in and which tools it offers.
public sealed class McpHostOptions
{
    public const string SectionName = "Mcp";

    public McpIdentityProvider Provider { get; set; } = McpIdentityProvider.EntraId;

    // The public URL of the MCP endpoint, the resource of the protected resource metadata; no trailing slash.
    public string ResourceUrl { get; set; } = string.Empty;

    public McpEntraIdOptions EntraId { get; set; } = new();

    public McpSecretToolsOptions SecretTools { get; set; } = new();
}
