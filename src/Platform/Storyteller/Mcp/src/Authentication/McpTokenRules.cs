using System.Security.Claims;

using _42.Platform.Storyteller.Mcp.Hosting;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// The checks of an inbound token that signature, issuer, audience and lifetime do not cover. Claims keep their JWT
// names (MapInboundClaims is off): scp, tid, azp, appid, idtyp.
public static class McpTokenRules
{
    // Null when the token may reach the MCP endpoint; otherwise why it may not (an authentication failure, 401).
    public static string? Check(ClaimsPrincipal? principal, McpAuthSettings settings)
    {
        if (principal is null)
        {
            return "The token has no claims.";
        }

        if (!string.Equals(principal.FindFirst("tid")?.Value, settings.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return "The token is not from the tenant of the MCP server.";
        }

        var clientId = principal.FindFirst("azp")?.Value ?? principal.FindFirst("appid")?.Value;

        if (clientId is null || !settings.AllowedClientIds.Contains(clientId))
        {
            return "The token was issued to a client app that the MCP server does not accept.";
        }

        if (string.Equals(principal.FindFirst("idtyp")?.Value, "app", StringComparison.OrdinalIgnoreCase)
            || GetScopes(principal).Count == 0)
        {
            return "The token does not act for a user.";
        }

        return null;
    }

    public static bool HasScope(ClaimsPrincipal principal, string scope)
    {
        return GetScopes(principal).Contains(scope);
    }

    public static IReadOnlySet<string> GetScopes(ClaimsPrincipal principal)
    {
        return principal.FindAll("scp")
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);
    }
}
