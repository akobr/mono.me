using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using _42.Platform.Storyteller.Access;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Azure.Functions.Worker.Http;

namespace _42.Platform.Storyteller.Api.Security;

public static class HttpRequestDataExtensions
{
    public static IEnumerable<Claim> GetClaims(this HttpRequestData @this)
    {
        // TODO: [P1] create claims map and cache it on the function context
        // @this.FunctionContext.Items.TryGetValue("ClaimsMap", out var claimMap);
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
#if NOAUTH
        return;
#endif

        var allScopes = @this.GetClaims()
            .Where(c => c.Type is "scp" or "roles")
            .SelectMany(c => c.Value.Split(' '))
            .Select(scope => scope.StartsWith("App.", StringComparison.OrdinalIgnoreCase) ? scope[4..] : scope)
            .ToHashSet();

        if (scopes.All(scope => !allScopes.Contains(scope)))
        {
            throw new AuthenticationFailureException("Missing scope(s): " + string.Join(", ", scopes));
        }
    }

    public static async Task CheckAccessToAsync(this HttpRequestData @this, IAccessService accessService, string accessPointKey, AccountRole minimalRole = AccountRole.Reader)
    {
        if (@this.IsApplicationIdentity())
        {
            return;
        }

        var accountKey = @this.GetUniqueIdentityName().ToNormalizedKey();
        var accessRole = await accessService.GetAccountRoleAsync(accountKey, accessPointKey);

        if (accessRole < minimalRole)
        {
            throw new UnauthorizedAccessException($"No {minimalRole:G} access to the project {accessPointKey}.");
        }
    }

    public static Task CheckAccessToOrganizationAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        AccountRole minimalRole = AccountRole.Reader)
    {
        return CheckAccessToAsync(@this, accessService, organization, minimalRole);
    }

    public static Task CheckAccessToProjectAsync(
        this HttpRequestData @this,
        IAccessService accessService,
        string organization,
        string project,
        AccountRole minimalRole = AccountRole.Reader)
    {
        return CheckAccessToAsync(@this, accessService, $"{organization}.{project}", minimalRole);
    }

    public static string GetUniqueIdentityName(this HttpRequestData @this)
    {
        return GetRequiredClaim(@this, "unique_name");
    }

    public static string GetIdentityName(this HttpRequestData @this)
    {
        return GetRequiredClaim(@this, "name");
    }

    public static bool IsApplicationIdentity(this HttpRequestData @this)
    {
        // TODO: [P1] find how to best detect application identity
        return @this.GetClaims().Any(c => c.Type == "appid");
    }

    public static string GetRequiredClaim(this HttpRequestData @this, string claimType)
    {
        var uniqueNameClaim = @this.GetClaims()
            .FirstOrDefault(c => c.Type == claimType);

        return uniqueNameClaim?.Value ?? throw new AuthenticationFailureException($"Missing {claimType} claim.");
    }
}
