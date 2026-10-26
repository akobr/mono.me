using System.Text.Json;
using System.Text.Json.Serialization;

using _42.Platform.Storyteller.Mcp.Hosting;

using ModelContextProtocol.Authentication;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// The discovery documents MCP clients read before they sign in. They are built from the options, so every URL is right
// behind a TLS-terminating proxy.
// - Protected resource metadata (RFC 9728): the resource, its scope and the authorization server: the AuthKit domain
//   or the Keycloak realm, which publish their own metadata, or under Entra ID the host itself.
// - Authorization server metadata (RFC 8414) on the host's own origin, under Entra ID only: Entra's endpoints, plus the
//   facts MCP clients require and Entra's own metadata does not publish (PKCE with S256). Entra issues every token.
//   Under the other providers the path answers 404.
public static class OAuthMetadataEndpoints
{
    public const string ResourceName = "2S Platform";
    public const string Documentation = "https://github.com/akobr/mono.me/blob/main/docs/Platform/Storyteller/mcp.md";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static IEndpointRouteBuilder MapOAuthMetadata(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(McpAuthSettings.ProtectedResourceMetadataPath, ProtectedResource).AllowAnonymous();
        endpoints.MapGet(McpAuthSettings.ProtectedResourceMetadataPath + "/{**path}", ProtectedResource).AllowAnonymous();
        endpoints.MapGet(McpAuthSettings.AuthorizationServerMetadataPath, AuthorizationServer).AllowAnonymous();
        return endpoints;
    }

    public static ProtectedResourceMetadata CreateProtectedResource(McpAuthSettings settings)
    {
        return new ProtectedResourceMetadata
        {
            Resource = settings.ResourceUrl,
            AuthorizationServers = [settings.AuthorizationServer],
            ScopesSupported = [settings.FullScope],
            BearerMethodsSupported = ["header"],
            ResourceName = ResourceName,
            ResourceDocumentation = Documentation,
        };
    }

    public static AuthorizationServerDocument CreateAuthorizationServer(McpAuthSettings settings)
    {
        return new AuthorizationServerDocument
        {
            Issuer = settings.Origin,
            AuthorizationEndpoint = $"{settings.Authority}/oauth2/v2.0/authorize",
            TokenEndpoint = $"{settings.Authority}/oauth2/v2.0/token",
            ScopesSupported = [settings.FullScope, "offline_access"],
        };
    }

    private static IResult ProtectedResource(McpAuthSettings settings)
    {
        return Results.Json(CreateProtectedResource(settings), SerializerOptions);
    }

    private static IResult AuthorizationServer(McpAuthSettings settings)
    {
        return settings.ServesAuthorizationServerMetadata
            ? Results.Json(CreateAuthorizationServer(settings), SerializerOptions)
            : Results.NotFound();
    }
}
