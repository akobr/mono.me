using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text;

using _42.Platform.Storyteller.Accessing;

using Microsoft.IdentityModel.Tokens;

using HttpRequestData = Microsoft.Azure.Functions.Worker.Http.HttpRequestData;

namespace _42.Platform.Storyteller.Api.Security;

public static class HttpRequestDataExtensions
{
    private const string NameClaimType = "name";

    private static readonly string[] UniqueNameClaimTypes = ["preferred_username", "unique_name", ClaimTypes.Upn];

    public static IReadOnlyList<Claim> GetClaims(this HttpRequestData @this)
    {
        @this.FunctionContext.Items.TryGetValue(FunctionContextItemKeys.CachedClaims, out var claimMap);

        if (claimMap is IReadOnlyList<Claim> cachedClaims)
        {
            return cachedClaims;
        }

        var identity = @this.Identities.FirstOrDefault(i => i.IsAuthenticated);

        if (identity is null)
        {
            return [];
        }

        var claims = identity.Claims.ToList();
        @this.FunctionContext.Items[FunctionContextItemKeys.CachedClaims] = claims;
        return claims;
    }

    public static void CheckScope(this HttpRequestData @this, params string[] scopes)
    {
#if DEV_AUTH
        return;
#endif
        var claims = @this.GetClaims();
        var allScopes = claims
            .Where(c => c.Type is "scp" or "roles" or ClaimTypes.Role || c.Type.EndsWith("/scope"))
            .SelectMany(c => c.Value.Split(' '))
            .Select(scope => scope.StartsWith("App.", StringComparison.OrdinalIgnoreCase) ? scope[4..] : scope)
            .ToHashSet();

        if (!scopes.Any(scope => allScopes.Contains(scope)))
        {
            // TODO: [P2] remove details from the exception message
            var allInfo = new StringBuilder();
            allInfo.AppendLine($"all available scopes: {string.Join(", ", allScopes)}");
            allInfo.AppendLine($"all claims: {string.Join("; ", claims.Select(c => $"{c.Type}={c.Value}"))}");
            allInfo.AppendLine($"identities: {string.Join("; ", @this.Identities.Select(i => $"{i.Name}|{i.IsAuthenticated}|{i.AuthenticationType}"))}");
            allInfo.AppendLine("Missing scope(s): " + string.Join(", ", scopes));
            throw new SecurityTokenException(allInfo.ToString());
        }
    }

    public static bool TryCheckScope(this HttpRequestData @this, string scope)
    {
#if DEV_AUTH
        return true;
#endif
        var claims = @this.GetClaims();
        var allScopes = claims
            .Where(c => c.Type is "scp" or "roles" or ClaimTypes.Role || c.Type.EndsWith("/scope"))
            .SelectMany(c => c.Value.Split(' '))
            .Select(scope => scope.StartsWith("App.", StringComparison.OrdinalIgnoreCase) ? scope[4..] : scope)
            .ToHashSet();

        return allScopes.Contains(scope);
    }

    public static string GetAuthor(this HttpRequestData @this)
    {
        if (@this.TryGetApplicationIdentity(out var appId))
        {
            return $"application: {appId}";
        }

        var accountId = @this.GetIdentityUniqueId();
        return $"account: {accountId}";
    }

    public static async Task CheckAccessToAsync(this HttpRequestData @this, IAccessService accessService, string accessPointKey, AccountRole minimalRole = AccountRole.Reader)
    {
#if DEV_AUTH
        return;
#else
        if (@this.TryGetApplicationIdentity(out var appId))
        {
            var segments = accessPointKey.Split('.', StringSplitOptions.None);

            if (segments.Length < 2)
            {
                throw new SecurityTokenException("An application can never access an organization.");
            }

            if (!await accessService.VerifyAccessForMachineAsync(segments[0], segments[1], appId))
            {
                throw new SecurityTokenException($"The application {appId} doesn't has access to the project {accessPointKey}.");
            }

            await MachineCredentialPolicy.EnsureAllowedAsync(@this.FunctionContext, segments[0], segments[1]);
            return;
        }

        var accountId = @this.GetIdentityUniqueId();
        var accessRole = await accessService.GetAccountRoleAsync(accountId, accessPointKey);

        if (accessRole < minimalRole)
        {
            throw new SecurityTokenException($"No {minimalRole:G} access to the project {accessPointKey}.");
        }
#endif
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

    public static string GetIdentityUniqueName(this HttpRequestData @this)
    {
        return GetRequiredClaim(@this, UniqueNameClaimTypes).Trim();
    }

    public static string GetIdentityName(this HttpRequestData @this)
    {
        return GetRequiredClaim(@this, NameClaimType).Trim();
    }

    // Token claims win. The provider's resolver (AuthKit only) fills what the token lacks,
    // so a deployment without a JWT template can still register accounts.
    public static async Task<(string UserName, string Name)> GetIdentityProfileAsync(
        this HttpRequestData @this,
        IUserProfileResolver? profileResolver)
    {
        var userName = @this.GetClaim(UniqueNameClaimTypes)?.Trim();
        var name = @this.GetClaim(NameClaimType)?.Trim();

        if (profileResolver is not null && (string.IsNullOrEmpty(userName) || string.IsNullOrEmpty(name)))
        {
            var profile = await profileResolver.ResolveAsync(@this.GetIdentityUniqueId(), @this.FunctionContext.CancellationToken);
            userName = string.IsNullOrEmpty(userName) ? profile?.UserName?.Trim() ?? userName : userName;
            name = string.IsNullOrEmpty(name) ? profile?.Name?.Trim() ?? name : name;
        }

        return (
            userName ?? throw new SecurityTokenException($"Missing {UniqueNameClaimTypes[0]} claim."),
            name ?? throw new SecurityTokenException($"Missing {NameClaimType} claim."));
    }

    public static string GetIdentityUniqueId(this HttpRequestData @this)
    {
        return GetRequiredClaim(@this, "sub", ClaimTypes.NameIdentifier).Trim();
    }

    public static bool IsApplicationIdentity(this HttpRequestData @this)
    {
        return TryGetApplicationIdentity(@this, out _);
    }

    public static bool TryGetApplicationIdentity(this HttpRequestData @this, [MaybeNullWhen(false)] out string appId)
    {
        if (@this.FunctionContext.Items.TryGetValue(FunctionContextItemKeys.MachineIdentity, out var value)
            && value is string machineId
            && !string.IsNullOrEmpty(machineId))
        {
            appId = machineId;
            return true;
        }

        appId = null;
        return false;
    }

    public static string? GetClaim(this HttpRequestData @this, params string[] claimTypes)
    {
        var claims = @this.GetClaims();

        foreach (var claimType in claimTypes)
        {
            var claim = claims.FirstOrDefault(c => c.Type == claimType);
            if (claim is not null)
            {
                return claim.Value;
            }
        }

        return null;
    }

    public static string GetRequiredClaim(this HttpRequestData @this, params string[] claimTypes)
    {
        var claims = @this.GetClaims();

        foreach (var claimType in claimTypes)
        {
            var claim = claims.FirstOrDefault(c => c.Type == claimType);
            if (claim is not null)
            {
                return claim.Value;
            }
        }

        throw new SecurityTokenException($"Missing {claimTypes[0]} claim.");
    }
}
