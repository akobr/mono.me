namespace _42.Platform.Storyteller.Mcp.Hosting;

// The values the sign-in derives from validated McpHostOptions: pinned issuers, the audience, the full scope and the
// URLs of both metadata documents. Every URL comes from the resource URL, never from a request's Host header.
public sealed class McpAuthSettings
{
    public const string ProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource";
    public const string AuthorizationServerMetadataPath = "/.well-known/oauth-authorization-server";

    private McpAuthSettings(McpHostOptions options)
    {
        var resource = new Uri(options.ResourceUrl, UriKind.Absolute);
        var entra = options.EntraId;

        TenantId = Guid.Parse(entra.TenantId).ToString("D");
        ClientId = Guid.Parse(entra.ClientId).ToString("D");
        ResourceUrl = options.ResourceUrl;
        Origin = resource.GetLeftPart(UriPartial.Authority);
        Audience = string.IsNullOrWhiteSpace(entra.Audience) ? options.ResourceUrl : entra.Audience.TrimEnd('/');
        Scope = entra.Scope;
        FullScope = $"{Audience}/{Scope}";
        AllowedClientIds = entra.AllowedClientIds.Select(id => Guid.Parse(id).ToString("D")).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Authority = $"https://login.microsoftonline.com/{TenantId}";
        ValidIssuers = [$"{Authority}/v2.0", $"https://sts.windows.net/{TenantId}/"];
        MetadataAddress = $"{Authority}/v2.0/.well-known/openid-configuration";
        ProtectedResourceMetadataUrl = $"{Origin}{ProtectedResourceMetadataPath}{resource.AbsolutePath.TrimEnd('/')}";
    }

    public string TenantId { get; }

    public string ClientId { get; }

    public string ResourceUrl { get; }

    // The MCP host's own origin, the issuer of the authorization server metadata it serves.
    public string Origin { get; }

    public string Audience { get; }

    public string Scope { get; }

    // The scope MCP clients request, for example https://mcp.example.com/mcp/Mcp.Access.
    public string FullScope { get; }

    public IReadOnlySet<string> AllowedClientIds { get; }

    public string Authority { get; }

    public IReadOnlyList<string> ValidIssuers { get; }

    public string MetadataAddress { get; }

    public string ProtectedResourceMetadataUrl { get; }

    public static McpAuthSettings From(McpHostOptions options)
    {
        return new McpAuthSettings(options);
    }
}
