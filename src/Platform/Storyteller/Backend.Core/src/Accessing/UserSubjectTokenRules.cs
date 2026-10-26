using System.Globalization;
using System.Security.Claims;

namespace _42.Platform.Storyteller.Accessing;

// The rules every IUserSubjectTokenValidator applies besides its provider's own. They read the validated claims as the
// token carries them (not mapped by JwtSecurityTokenHandler).
public static class UserSubjectTokenRules
{
    public const string ActorClaimType = "act";

    private static readonly long MaxUnixSeconds = DateTimeOffset.MaxValue.ToUnixTimeSeconds();

    // A refusal for a request no token can pass, or null.
    public static UserSubjectTokenResult? CheckRequest(string? subjectToken, IReadOnlyCollection<string> audiences)
    {
        ArgumentNullException.ThrowIfNull(audiences);

        if (string.IsNullOrWhiteSpace(subjectToken))
        {
            return UserSubjectTokenResult.Refused("The subject token is empty.");
        }

        // Without an audience to bind to, a token a person gave any app would pass.
        return audiences.Any(audience => !string.IsNullOrWhiteSpace(audience))
            ? null
            : UserSubjectTokenResult.Refused("The actor names no subject audience.");
    }

    // The audiences to validate the token's aud against.
    public static string[] GetAudiences(IReadOnlyCollection<string> audiences)
    {
        return audiences.Where(audience => !string.IsNullOrWhiteSpace(audience)).Select(audience => audience.Trim()).ToArray();
    }

    // A token that already names an actor (RFC 8693 act) is not exchanged: chained delegation is not supported.
    public static bool HasActor(IReadOnlyList<Claim> claims)
    {
        return claims.Any(claim => claim.Type == ActorClaimType);
    }

    // The client the token was issued to: azp, then appid (Entra ID v1) or client_id (RFC 9068, AuthKit).
    public static string? GetClientId(IReadOnlyList<Claim> claims)
    {
        return FirstValue(claims, "azp") ?? FirstValue(claims, "appid") ?? FirstValue(claims, "client_id");
    }

    // Any client when allowedClientIds lists none.
    public static bool IsAllowedClient(string? clientId, IReadOnlyCollection<string> allowedClientIds, StringComparison comparison = StringComparison.Ordinal)
    {
        var allowed = allowedClientIds.Where(allowedClientId => !string.IsNullOrWhiteSpace(allowedClientId)).ToList();
        return allowed.Count == 0
            || (clientId is not null && allowed.Any(allowedClientId => string.Equals(allowedClientId.Trim(), clientId, comparison)));
    }

    // jti, else the uti of Entra ID tokens.
    public static string? GetTokenId(IReadOnlyList<Claim> claims)
    {
        return FirstValue(claims, "jti") ?? FirstValue(claims, "uti");
    }

    // auth_time, in Unix seconds; null when absent or not a number.
    public static DateTimeOffset? GetAuthTime(IReadOnlyList<Claim> claims)
    {
        return long.TryParse(FirstValue(claims, "auth_time"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0
            && seconds <= MaxUnixSeconds
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }

    private static string? FirstValue(IReadOnlyList<Claim> claims, string type)
    {
        foreach (var claim in claims)
        {
            if (claim.Type == type && !string.IsNullOrWhiteSpace(claim.Value))
            {
                return claim.Value;
            }
        }

        return null;
    }
}
