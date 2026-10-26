using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Subject tokens of the user token exchange under Entra ID: a delegated user token (a non-blank scp, no idtyp=app, a GUID
// tid and oid) of the configured tenant, RS256, for one of the actor's audiences. The issuer is the tenant's v2
// (login.microsoftonline.com/{tid}/v2.0) or v1 (sts.windows.net/{tid}/) one. A GUID Auth:TenantId pins that tenant. When
// Auth:TenantId is common or organizations, the token's own tid names the tenant (organizations without personal
// Microsoft accounts). Another name that is not a GUID (a verified domain) is pinned to the tenant GUID of the resolved
// metadata issuer, and refused when that issuer names none. The signing keys, and their refresh on an unknown kid, are
// EntraIdBearerTokenValidator's.
public class EntraIdUserSubjectTokenValidator : IUserSubjectTokenValidator
{
    private const string TenantIdClaimType = "tid";
    private const string ScopeClaimType = "scp";
    private const string TokenKindClaimType = "idtyp";
    private const string MetadataHost = "login.microsoftonline.com";

    // The tenant of personal Microsoft accounts, which the organizations endpoint excludes.
    private static readonly Guid ConsumerTenantId = Guid.Parse("9188040d-6c67-4c5b-b112-36a304b66dad");

    private readonly EntraIdBearerTokenValidator _keys;
    private readonly Guid? _tenantId;
    private readonly bool _anyTenant;
    private readonly bool _excludeConsumers;

    public EntraIdUserSubjectTokenValidator(EntraIdBearerTokenValidator keys, IOptions<UserAuthenticationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(options);

        _keys = keys;
        var tenant = options.Value.TenantId?.Trim();
        _tenantId = Guid.TryParse(tenant, out var tenantId) ? tenantId : null;
        _excludeConsumers = string.Equals(tenant, "organizations", StringComparison.OrdinalIgnoreCase);
        _anyTenant = _excludeConsumers || string.Equals(tenant, "common", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<UserSubjectTokenResult> ValidateAsync(
        string subjectToken,
        IReadOnlyCollection<string> audiences,
        IReadOnlyCollection<string> allowedClientIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(allowedClientIds);

        if (UserSubjectTokenRules.CheckRequest(subjectToken, audiences) is { } refused)
        {
            return refused;
        }

        var handler = new JwtSecurityTokenHandler();

        if (!handler.CanReadToken(subjectToken))
        {
            return UserSubjectTokenResult.Refused("The subject token is not a JWT.");
        }

        try
        {
            return await _keys.ValidateWithSigningKeysAsync(
                configuration => Validate(handler, subjectToken, configuration, audiences, allowedClientIds),
                cancellationToken);
        }
        catch (SecurityTokenException exception)
        {
            return UserSubjectTokenResult.NotValid(exception);
        }
        catch (ArgumentException exception)
        {
            return UserSubjectTokenResult.NotValid(exception);
        }
    }

    private UserSubjectTokenResult Validate(
        JwtSecurityTokenHandler handler,
        string subjectToken,
        OpenIdConnectConfiguration configuration,
        IReadOnlyCollection<string> audiences,
        IReadOnlyCollection<string> allowedClientIds)
    {
        // A domain name resolves to the metadata of one tenant; common and organizations to the templated {tenantid}.
        var pinned = _tenantId ?? (_anyTenant ? null : TenantOfIssuer(configuration.Issuer));

        if (!_anyTenant && pinned is null)
        {
            return UserSubjectTokenResult.Refused("The configured tenant could not be resolved from the OpenID metadata.");
        }

        // Entra keeps the handler default clock skew of five minutes, as on API routes.
        var validationParameters = new TokenValidationParameters
        {
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidAudiences = UserSubjectTokenRules.GetAudiences(audiences),
            IssuerSigningKeys = configuration.SigningKeys,
            IssuerValidator = (issuer, securityToken, _) => ValidateIssuer(issuer, securityToken, pinned, _excludeConsumers),
            ValidateLifetime = true,
            RequireExpirationTime = true,
        };

        var principal = handler.ValidateToken(subjectToken, validationParameters, out var validatedToken);

        // The token's own claim names; the principal's are mapped (sub is NameIdentifier, tid a schema URI).
        var token = (JwtSecurityToken)validatedToken;
        var rawClaims = token.Claims.ToList();

        if (UserSubjectTokenRules.HasActor(rawClaims))
        {
            return UserSubjectTokenResult.Refused("The token already names an actor (act).");
        }

        if (string.Equals(FirstValue(rawClaims, TokenKindClaimType), "app", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(FirstValue(rawClaims, ScopeClaimType)))
        {
            return UserSubjectTokenResult.Refused("The token is not a delegated user token (no scp, or idtyp app).");
        }

        // The claims and the account id {tid}:{oid} that API routes get for the same person.
        var claims = EntraIdBearerTokenValidator.KeyByAccountId(principal.Claims.ToList(), out var accountId);

        if (accountId is null)
        {
            return UserSubjectTokenResult.Refused("The token has no GUID tid and oid.");
        }

        var clientId = UserSubjectTokenRules.GetClientId(rawClaims);

        // Entra writes client ids in the dashed lower-case form; settings may not.
        if (!UserSubjectTokenRules.IsAllowedClient(clientId, allowedClientIds, StringComparison.OrdinalIgnoreCase))
        {
            return UserSubjectTokenResult.Refused("The token's client (azp or appid) is not one of the actor's AllowedClientIds.");
        }

        return UserSubjectTokenResult.Accepted(
            accountId,
            clientId,
            new DateTimeOffset(token.ValidTo, TimeSpan.Zero),
            UserSubjectTokenRules.GetAuthTime(rawClaims),
            UserSubjectTokenRules.GetTokenId(rawClaims),
            claims);
    }

    // The v2 or v1 issuer of the token's tenant. That tenant is the pinned one: the GUID of Auth:TenantId, or the tenant
    // of the metadata issuer for a verified domain. Under common or organizations nothing is pinned, and the token's
    // own tid names the tenant; organizations refuses the tenant of personal Microsoft accounts.
    private static string ValidateIssuer(string issuer, SecurityToken securityToken, Guid? pinned, bool excludeConsumers)
    {
        var tenant = securityToken is JwtSecurityToken token ? FirstValue(token.Claims.ToList(), TenantIdClaimType) : null;

        if (!Guid.TryParse(tenant, out var tenantId)
            || (pinned is { } configured && configured != tenantId)
            || (excludeConsumers && tenantId == ConsumerTenantId))
        {
            throw new SecurityTokenInvalidIssuerException("The token is not of the configured tenant.");
        }

        if (string.Equals(issuer, $"https://login.microsoftonline.com/{tenantId:D}/v2.0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(issuer, $"https://sts.windows.net/{tenantId:D}/", StringComparison.OrdinalIgnoreCase))
        {
            return issuer;
        }

        throw new SecurityTokenInvalidIssuerException("The issuer is not the v2 or v1 issuer of the token's tenant.");
    }

    // The tenant of a metadata issuer https://login.microsoftonline.com/{guid}/v2.0, else null (the templated {tenantid}
    // of common and organizations, or anything else).
    private static Guid? TenantOfIssuer(string? issuer)
    {
        if (!Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, MetadataHost, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort)
        {
            return null;
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 && segments[1] == "v2.0" && Guid.TryParseExact(segments[0], "D", out var tenantId) ? tenantId : null;
    }

    private static string? FirstValue(IReadOnlyList<Claim> claims, string type)
    {
        return claims.FirstOrDefault(claim => claim.Type == type)?.Value;
    }
}
