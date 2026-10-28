using System.Security.Claims;

using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Validates Keycloak user access tokens of the realm Auth:Keycloak:Issuer: keys from the realm's OpenID metadata,
// audience Auth:Keycloak:Audience, RS256, and a client (azp) of Auth:Keycloak:AllowedClientIds, which is required.
// Service accounts and ID tokens are not users; the tokens of machine clients are KeycloakMachineTokenValidator's.
public class KeycloakBearerTokenValidator : IBearerTokenValidator
{
    private const string AccessTokenType = "Bearer";

    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly string _issuer;
    private readonly string _audience;
    private readonly IReadOnlySet<string> _allowedClientIds;
    private readonly KeycloakClaimNormalizer _normalizer;
    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
    private readonly JsonWebTokenHandler _handler = new();

    public KeycloakBearerTokenValidator(IOptions<UserAuthenticationOptions> options, KeycloakClaimNormalizer normalizer)
        : this(options, normalizer, KeycloakMachineTokenValidator.CreateConfigurationManager(options.Value.Keycloak.GetIssuer()))
    {
    }

    public KeycloakBearerTokenValidator(
        IOptions<UserAuthenticationOptions> options,
        KeycloakClaimNormalizer normalizer,
        IConfigurationManager<OpenIdConnectConfiguration> configurationManager)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(normalizer);
        ArgumentNullException.ThrowIfNull(configurationManager);

        var keycloak = options.Value.Keycloak;

        if (string.IsNullOrWhiteSpace(keycloak.GetIssuer()) || string.IsNullOrWhiteSpace(keycloak.Audience))
        {
            throw new InvalidOperationException("Auth:Keycloak:Issuer and Auth:Keycloak:Audience are required for Keycloak token validation.");
        }

        _issuer = keycloak.GetIssuer();
        _audience = keycloak.Audience;
        _allowedClientIds = keycloak.AllowedClientIds
            .Where(clientId => !string.IsNullOrWhiteSpace(clientId))
            .Select(clientId => clientId.Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (_allowedClientIds.Count == 0)
        {
            throw new InvalidOperationException("Auth:Keycloak:AllowedClientIds must list at least one client for Keycloak token validation.");
        }

        _normalizer = normalizer;
        _configurationManager = configurationManager;
    }

    public async Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        // A token of another issuer (a machine token of another provider, for example) is not this validator's. No
        // keys are fetched for it, so an unreachable realm does not answer 503 to those tokens.
        if (!IssuerMatches(TryReadIssuer(rawToken)))
        {
            return null;
        }

        var result = await ValidateTokenAsync(rawToken, [_audience], cancellationToken);

        if (!result.IsValid)
        {
            return null;
        }

        var claims = result.ClaimsIdentity.Claims.ToList();
        return IsPersonToken(claims) && IsAllowedClient(claims) ? _normalizer.NormalizeUser(claims) : null;
    }

    // The realm's keys, issuer and lifetime rules with the given audiences; the result keeps the reason of a refusal.
    // KeycloakUserSubjectTokenValidator shares them. Throws BearerKeyRetrievalException when the keys cannot be retrieved.
    internal async Task<TokenValidationResult> ValidateTokenAsync(
        string rawToken,
        IReadOnlyCollection<string> audiences,
        CancellationToken cancellationToken)
    {
        OpenIdConnectConfiguration configuration;

        try
        {
            configuration = await _configurationManager.GetConfigurationAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or IOException or OperationCanceledException or ArgumentException)
        {
            throw new BearerKeyRetrievalException("The Keycloak signing keys could not be retrieved.", exception);
        }

        var result = await _handler.ValidateTokenAsync(rawToken, new TokenValidationParameters
        {
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            IssuerSigningKeys = configuration.SigningKeys,
            IssuerValidator = (issuer, _, _) => IssuerMatches(issuer)
                ? issuer
                : throw new SecurityTokenInvalidIssuerException($"Invalid issuer: {issuer}"),
            ValidAudiences = audiences,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = ClockSkew,
        });

        if (!result.IsValid && result.Exception is SecurityTokenSignatureKeyNotFoundException)
        {
            // A rotated key: the next request fetches the metadata again (rate limited by the manager).
            _configurationManager.RequestRefresh();
        }

        return result;
    }

    // A person of the realm: a sub (a lightweight token, or one without the basic client scope, has none), not a
    // service account, and an access token: typ Bearer, which Keycloak puts on every access token (an ID token says ID, and
    // a token without typ is refused). Whose client may send it is a separate gate.
    internal static bool IsPersonToken(IReadOnlyList<Claim> claims)
    {
        var tokenType = FindValue(claims, "typ");

        return !string.IsNullOrWhiteSpace(FindValue(claims, "sub"))
            && !KeycloakClaimNormalizer.IsServiceAccount(claims)
            && string.Equals(tokenType, AccessTokenType, StringComparison.OrdinalIgnoreCase);
    }

    // Unverified; only decides whether this validator checks the token. Null when unreadable.
    internal static string? TryReadIssuer(string rawToken)
    {
        var handler = new JsonWebTokenHandler();

        if (string.IsNullOrWhiteSpace(rawToken) || !handler.CanReadToken(rawToken))
        {
            return null;
        }

        try
        {
            return handler.ReadJsonWebToken(rawToken).Issuer;
        }
        catch (Exception exception) when (exception is ArgumentException or SecurityTokenException)
        {
            return null;
        }
    }

    // The realm issuer, exactly; only a trailing '/' may differ.
    internal bool IssuerMatches(string? issuer)
    {
        return issuer is not null && string.Equals(issuer.TrimEnd('/'), _issuer, StringComparison.Ordinal);
    }

    private static string? FindValue(IReadOnlyList<Claim> claims, string claimType)
    {
        return claims.FirstOrDefault(claim => claim.Type == claimType)?.Value;
    }

    // The client the person signed in through (azp) is one of AllowedClientIds.
    private bool IsAllowedClient(IReadOnlyList<Claim> claims)
    {
        var clientId = FindValue(claims, "azp");
        return clientId is not null && _allowedClientIds.Contains(clientId);
    }
}
