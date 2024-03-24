using System.Diagnostics.CodeAnalysis;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using _42.Platform.Storyteller.Access;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using HttpRequestData = Microsoft.Azure.Functions.Worker.Http.HttpRequestData;

namespace _42.Platform.Storyteller.Api.Security;

public static class HttpRequestDataExtensions
{
    private static readonly string ClientId = Environment.GetEnvironmentVariable("Auth:ClientId");

    public static IReadOnlyList<Claim> GetClaims(this HttpRequestData @this)
    {
        @this.FunctionContext.Items.TryGetValue(FunctionContextItemKeys.CachedClaims, out var claimMap);

        if (claimMap is List<Claim> cachedClaims)
        {
            return cachedClaims;
        }

        var claims = new List<Claim>(0);
        var identity = @this.Identities.FirstOrDefault(i => i.IsAuthenticated);
        if (identity is not null)
        {
            claims = identity.Claims.ToList();
            @this.FunctionContext.Items[FunctionContextItemKeys.CachedClaims] = claims;
            return claims;
        }

        var handler = new JwtSecurityTokenHandler();
        @this.Headers.TryGetValues("Authorization", out var values);
        var bearerValue = values?.FirstOrDefault(v => v.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(bearerValue))
        {
            return claims;
        }

        var rawToken = bearerValue[7..];

        if (!handler.CanReadToken(rawToken))
        {
            return claims;
        }

#if !DEV_AUTH
        var principal = handler.ValidateAccessToken(rawToken);
        claims = principal.Claims.ToList();
#else
        var token = handler.ReadJwtToken(rawToken);
        claims = token.Claims.ToList();
#endif

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

        if (scopes.All(scope => !allScopes.Contains(scope)))
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

    public static async Task CheckAccessToAsync(this HttpRequestData @this, IAccessService accessService, string accessPointKey, AccountRole minimalRole = AccountRole.Reader)
    {
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

            return;
        }

        var accountKey = @this.GetUniqueIdentityName().ToNormalizedKey();
        var accessRole = await accessService.GetAccountRoleAsync(accountKey, accessPointKey);

        if (accessRole < minimalRole)
        {
            throw new SecurityTokenException($"No {minimalRole:G} access to the project {accessPointKey}.");
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
        return GetRequiredClaim(@this, "unique_name", ClaimTypes.Upn);
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

    public static bool TryGetApplicationIdentity(this HttpRequestData @this, [MaybeNullWhen(false)]out string appId)
    {
        var claims = @this.GetClaims();
        var appClaim = claims.FirstOrDefault(c => c.Type == "appid");
        appId = appClaim?.Value;

        return appId is not null
               && !string.Equals(appId, ClientId, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetRequiredClaim(this HttpRequestData @this, params string[] claimTypes)
    {
        var claims = @this.GetClaims();

        foreach (var claimType in claimTypes)
        {
            var uniqueNameClaim = claims.FirstOrDefault(c => c.Type == claimType);
            if (uniqueNameClaim is not null)
            {
                return uniqueNameClaim.Value;
            }
        }

        throw new SecurityTokenException($"Missing {claimTypes[0]} claim.");
    }

    private static ClaimsPrincipal ValidateAccessToken(this ISecurityTokenValidator @this, string accessToken)
    {
        var tenantId = Environment.GetEnvironmentVariable("Auth:TenantId");
        var clientId = Environment.GetEnvironmentVariable("Auth:ClientId");
        var audience = $"api://{clientId}";
        var authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";

        // Debugging purposes only, set this to false for production
        Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;

        var configManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            $"{authority}/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever());

        // Initialize the token validation parameters
        var validationParameters = new TokenValidationParameters
        {
            // App Id URI and AppId of this service application are both valid audiences.
            ValidAudiences = new[] { audience, clientId },

            // Support Azure AD V1 and V2 endpoints.
            IssuerValidator = (issuer, _, _) =>
            {
                if (issuer.StartsWith("https://sts.windows.net/", StringComparison.OrdinalIgnoreCase))
                {
                    return issuer;
                }

                throw new SecurityTokenInvalidIssuerException($"Invalid issuer: {issuer}");
            },
            ConfigurationManager = configManager,
        };

        SecurityToken securityToken;
        var claimsPrincipal = @this.ValidateToken(accessToken, validationParameters, out securityToken);
        return claimsPrincipal;
    }
}
