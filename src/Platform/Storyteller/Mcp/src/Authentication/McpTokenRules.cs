using System.Security.Claims;

using _42.Platform.Storyteller.Mcp.Hosting;

using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Mcp.Authentication;

// The checks of an inbound token that signature, issuer, audience and lifetime do not cover: the token acts for a
// person, through a client the server accepts, and lives long enough for the API's token exchange. Claims keep their
// JWT names (MapInboundClaims is off).
public static class McpTokenRules
{
    public const string NotAUserMessage = "The token does not act for a user.";
    public const string ClientNotAcceptedMessage = "The token was issued to a client app that the MCP server does not accept.";
    public const string ExpiresTooSoonMessage = "The token expires too soon to be exchanged for a Storyteller token.";

    // The clock skew of inbound tokens, the API's: its MachineAuth:Federation default and its provider validators' skew.
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    // The least lifetime an inbound token has left. The API exchanges no subject token with less than 5 seconds left (its
    // MinSubjectTokenLifetime); the rest leaves room for the exchange's round trip and the clocks of the two servers.
    public static readonly TimeSpan MinRemainingLifetime = TimeSpan.FromSeconds(10);

    private const string AuthKitUserIdPrefix = "user_";
    private const string KeycloakServiceAccountPrefix = "service-account-";
    private const string KeycloakAccessTokenType = "Bearer";

    // Null when the token may reach the MCP endpoint; otherwise why it may not (an authentication failure, 401).
    public static string? Check(ClaimsPrincipal? principal, McpAuthSettings settings)
    {
        if (principal is null)
        {
            return "The token has no claims.";
        }

        return settings.Provider switch
        {
            McpIdentityProvider.EntraId => CheckEntraId(principal, settings),
            McpIdentityProvider.AuthKit => CheckAuthKit(principal, settings),
            McpIdentityProvider.Keycloak => CheckKeycloak(principal, settings),
            _ => "The MCP server has no rules for its identity provider.",
        };
    }

    // Null when the token lives at least MinRemainingLifetime longer; otherwise why it may not reach the MCP endpoint (a
    // 401, so the client gets a new token instead of calling with one the API's exchange refuses).
    public static string? CheckLifetime(SecurityToken token, DateTimeOffset now)
    {
        return new DateTimeOffset(token.ValidTo, TimeSpan.Zero) - now < MinRemainingLifetime ? ExpiresTooSoonMessage : null;
    }

    public static bool HasScope(ClaimsPrincipal principal, McpIdentityProvider provider, string scope)
    {
        return GetScopes(principal, provider).Contains(scope);
    }

    // scp (Entra ID) and scope (RFC 9068, Keycloak) are space-separated strings; AuthKit lists its permissions one per
    // claim. Permissions count only under AuthKit, where they are what the scope check is about.
    public static IReadOnlySet<string> GetScopes(ClaimsPrincipal principal, McpIdentityProvider provider)
    {
        return principal.Claims
            .Where(claim => claim.Type is "scp" or "scope" || (provider == McpIdentityProvider.AuthKit && claim.Type == "permissions"))
            .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.Ordinal);
    }

    // One tenant (tid), a listed client app (azp, or appid in v1 tokens) and a delegated token: scp, no idtyp app.
    private static string? CheckEntraId(ClaimsPrincipal principal, McpAuthSettings settings)
    {
        if (!string.Equals(principal.FindFirst("tid")?.Value, settings.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return "The token is not from the tenant of the MCP server.";
        }

        var clientId = principal.FindFirst("azp")?.Value ?? principal.FindFirst("appid")?.Value;

        if (clientId is null || !settings.AllowedClientIds.Contains(clientId))
        {
            return ClientNotAcceptedMessage;
        }

        if (string.Equals(principal.FindFirst("idtyp")?.Value, "app", StringComparison.OrdinalIgnoreCase)
            || !principal.FindAll("scp").Any(claim => !string.IsNullOrWhiteSpace(claim.Value)))
        {
            return NotAUserMessage;
        }

        return null;
    }

    // A WorkOS user (sub user_…; an M2M token names its application instead), through an accepted client (client_id,
    // or azp) when AllowedClientIds lists any.
    private static string? CheckAuthKit(ClaimsPrincipal principal, McpAuthSettings settings)
    {
        if (principal.FindFirst("sub")?.Value?.StartsWith(AuthKitUserIdPrefix, StringComparison.Ordinal) != true)
        {
            return NotAUserMessage;
        }

        var clientId = principal.FindFirst("client_id")?.Value ?? principal.FindFirst("azp")?.Value;
        return IsAcceptedClient(clientId, settings) ? null : ClientNotAcceptedMessage;
    }

    // A person of the realm: a sub, not a client's service account (client_id, clientId in older versions, or the user
    // service-account-{client}), an access token (typ Bearer when present; an ID token says ID), through an accepted
    // client (azp) when AllowedClientIds lists any.
    private static string? CheckKeycloak(ClaimsPrincipal principal, McpAuthSettings settings)
    {
        if (string.IsNullOrWhiteSpace(principal.FindFirst("sub")?.Value)
            || principal.HasClaim(claim => claim.Type is "client_id" or "clientId")
            || principal.FindFirst("preferred_username")?.Value?.StartsWith(KeycloakServiceAccountPrefix, StringComparison.OrdinalIgnoreCase) == true)
        {
            return NotAUserMessage;
        }

        var tokenType = principal.FindFirst("typ")?.Value;

        if (tokenType is not null && !string.Equals(tokenType, KeycloakAccessTokenType, StringComparison.OrdinalIgnoreCase))
        {
            return "The token is not an access token.";
        }

        return IsAcceptedClient(principal.FindFirst("azp")?.Value, settings) ? null : ClientNotAcceptedMessage;
    }

    private static bool IsAcceptedClient(string? clientId, McpAuthSettings settings)
    {
        return settings.AllowedClientIds.Count == 0 || (clientId is not null && settings.AllowedClientIds.Contains(clientId));
    }
}
