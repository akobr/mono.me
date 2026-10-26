using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller;

// Subject tokens of the user token exchange under Keycloak: an access token of a person of the realm
// Auth:Keycloak:Issuer (KeycloakBearerTokenValidator's person rules: a sub, no service account, typ Bearer when present),
// RS256, 30 s clock skew, and an aud of the actor's audiences. The realm's keys are the bearer validator's. The client
// (azp) is checked against the actor's AllowedClientIds, not Auth:Keycloak:AllowedClientIds: the person signed in to
// the actor, not to sform.
public class KeycloakUserSubjectTokenValidator : IUserSubjectTokenValidator
{
    private readonly KeycloakBearerTokenValidator _validator;
    private readonly KeycloakClaimNormalizer _normalizer;

    public KeycloakUserSubjectTokenValidator(KeycloakBearerTokenValidator validator, KeycloakClaimNormalizer normalizer)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(normalizer);

        _validator = validator;
        _normalizer = normalizer;
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

        // No keys are fetched for a token of another issuer.
        if (!_validator.IssuerMatches(KeycloakBearerTokenValidator.TryReadIssuer(subjectToken)))
        {
            return UserSubjectTokenResult.Refused("The token's issuer is not the realm Auth:Keycloak:Issuer.");
        }

        var result = await _validator.ValidateTokenAsync(subjectToken, UserSubjectTokenRules.GetAudiences(audiences), cancellationToken);

        if (!result.IsValid)
        {
            return UserSubjectTokenResult.NotValid(result.Exception);
        }

        var rawClaims = result.ClaimsIdentity.Claims.ToList();

        if (UserSubjectTokenRules.HasActor(rawClaims))
        {
            return UserSubjectTokenResult.Refused("The token already names an actor (act).");
        }

        if (!KeycloakBearerTokenValidator.IsPersonToken(rawClaims))
        {
            return UserSubjectTokenResult.Refused("The token is not a person's access token (no sub, a service account, or not typ Bearer).");
        }

        var clientId = rawClaims.FirstOrDefault(claim => claim.Type == "azp")?.Value;

        if (!UserSubjectTokenRules.IsAllowedClient(clientId, allowedClientIds))
        {
            return UserSubjectTokenResult.Refused("The token's client (azp) is not one of the actor's AllowedClientIds.");
        }

        var claims = _normalizer.NormalizeUser(rawClaims).Claims;
        var accountId = UserIdentity.GetAccountId(claims, IdentityProviderKind.Keycloak);

        if (accountId is null)
        {
            return UserSubjectTokenResult.Refused("The token names no account (sub).");
        }

        return UserSubjectTokenResult.Accepted(
            accountId,
            clientId,
            new DateTimeOffset(result.SecurityToken.ValidTo, TimeSpan.Zero),
            UserSubjectTokenRules.GetAuthTime(rawClaims),
            UserSubjectTokenRules.GetTokenId(rawClaims),
            claims);
    }
}
