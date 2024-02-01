using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.Security;

public static class HttpRequestDataExtensions
{
    public static IEnumerable<Claim> GetClaims(this HttpRequestData @this)
    {
        foreach (var identity in @this.Identities.Where(i => i.IsAuthenticated))
        {
            foreach (var claim in identity.Claims)
            {
                yield return claim;
            }
        }

#if DEBUG
        var handler = new JwtSecurityTokenHandler();
        @this.Headers.TryGetValues("Authorization", out var values);

        foreach (var authorizationHeaderValue in values ?? Enumerable.Empty<string>())
        {
            var token = handler.ReadJwtToken(authorizationHeaderValue.Split(' ').Last());

            foreach (var claim in token.Claims)
            {
                yield return claim;
            }
        }
#endif
    }

    public static void CheckScope(this HttpRequestData @this, params string[] scopes)
    {
        var allScopes = @this.GetClaims()
            .Where(c => c.Type is "scp" or "roles")
            .SelectMany(c => c.Value.Split(' '))
            .Select(scope => scope.EndsWith(".All", StringComparison.OrdinalIgnoreCase) ? scope[..^4] : scope)
            .ToHashSet();

        if (scopes.All(scope => !allScopes.Contains(scope)))
        {
            throw new AuthenticationFailureException("Missing scope(s): " + string.Join(", ", scopes));
        }
    }

    public static string GetUniqueIdentityName(this HttpRequestData @this)
    {
        return GetRequiredClaim(@this, "unique_name");
    }

    public static string GetIdentityName(this HttpRequestData @this)
    {
        return GetRequiredClaim(@this, "name");
    }

    public static string GetRequiredClaim(this HttpRequestData @this, string claimType)
    {
        var uniqueNameClaim = @this.GetClaims()
            .FirstOrDefault(c => c.Type == claimType);

        return uniqueNameClaim?.Value ?? throw new AuthenticationFailureException($"Missing {claimType} claim.");
    }
}
