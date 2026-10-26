using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Subject tokens of the user token exchange under AuthKit: user tokens (sub user_…) of Auth:AuthKit:Issuer, with the
// bearer validator's key set, or of an Auth:TokenExchange:AdditionalIssuers entry (the AuthKit domain that issues MCP
// sign-in tokens), with keys from its JwksUri. RS256, 30 s clock skew, and an aud of the actor's audiences.
public class AuthKitUserSubjectTokenValidator : IUserSubjectTokenValidator
{
    private const string UserIdPrefix = "user_";

    private readonly IReadOnlyList<AuthKitTokenSource> _sources;
    private readonly AuthKitClaimNormalizer _normalizer;

    public AuthKitUserSubjectTokenValidator(
        AuthKitBearerTokenValidator validator,
        AuthKitClaimNormalizer normalizer,
        IOptions<TokenExchangeOptions> options)
        : this(validator, normalizer, options, AuthKitTokenSource.CreateKeyManager)
    {
    }

    internal AuthKitUserSubjectTokenValidator(
        AuthKitBearerTokenValidator validator,
        AuthKitClaimNormalizer normalizer,
        IOptions<TokenExchangeOptions> options,
        Func<string, IConfigurationManager<JsonWebKeySet>> createKeyManager)
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(normalizer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createKeyManager);

        // One key set per issuer. The URLs are operator configuration, never taken from a token.
        _sources =
        [
            validator.Source,
            .. options.Value.AdditionalIssuers.Select(issuer => new AuthKitTokenSource(
                issuer.Issuer.Trim(),
                audience: null,
                createKeyManager(issuer.JwksUri.Trim()))),
        ];
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

        // Picks the source whose keys and rules then check the token; no keys are fetched for another issuer.
        var issuer = AuthKitTokenSource.TryReadIssuer(subjectToken);

        if (issuer is null)
        {
            return UserSubjectTokenResult.Refused("The subject token is not a JWT with an issuer.");
        }

        var source = _sources.FirstOrDefault(candidate => AuthKitTokenSource.IssuerMatches(issuer, candidate.Issuer));

        if (source is null)
        {
            return UserSubjectTokenResult.Refused("The token's issuer is not Auth:AuthKit:Issuer or one of Auth:TokenExchange:AdditionalIssuers.");
        }

        var result = await source.ValidateTokenAsync(subjectToken, UserSubjectTokenRules.GetAudiences(audiences), cancellationToken);

        if (!result.IsValid)
        {
            return UserSubjectTokenResult.NotValid(result.Exception);
        }

        var rawClaims = result.ClaimsIdentity.Claims.ToList();

        if (UserSubjectTokenRules.HasActor(rawClaims))
        {
            return UserSubjectTokenResult.Refused("The token already names an actor (act).");
        }

        // A WorkOS user. An M2M token of the AuthKit domain names its application instead.
        var subject = rawClaims.FirstOrDefault(claim => claim.Type == "sub")?.Value;

        if (subject is null || !subject.StartsWith(UserIdPrefix, StringComparison.Ordinal))
        {
            return UserSubjectTokenResult.Refused("The token's sub is not a WorkOS user id (user_…).");
        }

        // From the validated claims: the normalizer drops azp, and AuthKit names the client in client_id.
        var clientId = UserSubjectTokenRules.GetClientId(rawClaims);

        if (!UserSubjectTokenRules.IsAllowedClient(clientId, allowedClientIds))
        {
            return UserSubjectTokenResult.Refused("The token's client (client_id) is not one of the actor's AllowedClientIds.");
        }

        var claims = _normalizer.NormalizeUser(rawClaims).Claims;
        var accountId = UserIdentity.GetAccountId(claims, IdentityProviderKind.AuthKit);

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
