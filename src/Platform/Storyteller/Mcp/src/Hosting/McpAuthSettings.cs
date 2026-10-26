namespace _42.Platform.Storyteller.Mcp.Hosting;

// The values the sign-in derives from validated McpHostOptions: pinned issuers, the audiences, the full scope and the
// URLs of the metadata documents. Every URL comes from the options, never from a request's Host header.
// - Entra ID: the issuers of one tenant (v2 and v1), and the host itself as the authorization server, whose RFC 8414
//   document adds what Entra's own metadata does not publish.
// - AuthKit and Keycloak: the configured issuer, which is also the authorization server MCP clients sign in with.
public sealed class McpAuthSettings
{
    public const string ProtectedResourceMetadataPath = "/.well-known/oauth-protected-resource";
    public const string AuthorizationServerMetadataPath = "/.well-known/oauth-authorization-server";
    public const string EntraLoginUrl = "https://login.microsoftonline.com";

    private McpAuthSettings(McpHostOptions options)
    {
        var resource = new Uri(options.ResourceUrl, UriKind.Absolute);
        var authentication = options.Authentication;

        Provider = options.Provider;
        ResourceUrl = options.ResourceUrl;
        Origin = resource.GetLeftPart(UriPartial.Authority);
        Audience = string.IsNullOrWhiteSpace(authentication.Audience) ? options.ResourceUrl : authentication.Audience.TrimEnd('/');
        Scope = authentication.Scope;
        ProtectedResourceMetadataUrl = $"{Origin}{ProtectedResourceMetadataPath}{resource.AbsolutePath.TrimEnd('/')}";

        if (Provider == McpIdentityProvider.EntraId)
        {
            var tenantId = Guid.Parse(authentication.TenantId!);

            TenantId = tenantId.ToString("D");
            ClientId = Guid.Parse(authentication.ClientId!).ToString("D");
            Authority = $"{EntraLoginUrl}/{TenantId}";
            Issuer = GetEntraIssuer(tenantId);
            ValidIssuers = [Issuer, $"https://sts.windows.net/{TenantId}/"];
            ValidAudiences = [ClientId, Audience];
            FullScope = $"{Audience}/{Scope}";
            AllowedClientIds = authentication.AllowedClientIds.Select(id => Guid.Parse(id).ToString("D")).ToHashSet(StringComparer.OrdinalIgnoreCase);
            AuthorizationServer = Origin;
        }
        else
        {
            Issuer = authentication.Issuer!.Trim();
            ValidIssuers = [Issuer];
            ValidAudiences = [Audience];
            FullScope = Scope;
            AllowedClientIds = authentication.AllowedClientIds.Select(id => id.Trim()).ToHashSet(StringComparer.Ordinal);
            AuthorizationServer = Issuer;
        }

        MetadataAddress = $"{Issuer}/.well-known/openid-configuration";
    }

    public McpIdentityProvider Provider { get; }

    // Entra ID only: the pinned tenant (tid) and the MCP app registration.
    public string? TenantId { get; }

    public string? ClientId { get; }

    public string ResourceUrl { get; }

    // The MCP host's own origin, the issuer of the authorization server metadata it serves for Entra ID.
    public string Origin { get; }

    public string Audience { get; }

    public string Scope { get; }

    // The scope MCP clients request: {Audience}/{Scope} under Entra ID, for example
    // https://mcp.example.com/mcp/Mcp.Access, and the scope itself under AuthKit and Keycloak.
    public string FullScope { get; }

    // Empty means any client (AuthKit and Keycloak only; Entra ID always lists them).
    public IReadOnlySet<string> AllowedClientIds { get; }

    // Entra ID only: the tenant's authority, whose endpoints the host's RFC 8414 document names.
    public string? Authority { get; }

    // The issuer of the OpenID metadata: the tenant's v2 issuer under Entra ID, otherwise the configured one.
    public string Issuer { get; }

    public IReadOnlyList<string> ValidIssuers { get; }

    // The aud values an inbound token may carry: under Entra ID the MCP app's client id (v2 tokens) and Audience (v1
    // tokens, the App ID URI), otherwise Audience. Every one of these must be in the API actor's
    // Auth:TokenExchange:Actors[].SubjectAudiences; otherwise the exchange refuses the token with invalid_grant.
    // McpStartupNotice logs them at start.
    public IReadOnlyList<string> ValidAudiences { get; }

    public string MetadataAddress { get; }

    // The authorization server the protected resource metadata names.
    public string AuthorizationServer { get; }

    // Only Entra ID needs the host's own RFC 8414 document; AuthKit and Keycloak publish theirs, with PKCE methods and
    // dynamic client registration.
    public bool ServesAuthorizationServerMetadata => Provider == McpIdentityProvider.EntraId;

    public string ProtectedResourceMetadataUrl { get; }

    public static McpAuthSettings From(McpHostOptions options)
    {
        return new McpAuthSettings(options);
    }

    public static string GetEntraIssuer(Guid tenantId)
    {
        return $"{EntraLoginUrl}/{tenantId:D}/v2.0";
    }
}
