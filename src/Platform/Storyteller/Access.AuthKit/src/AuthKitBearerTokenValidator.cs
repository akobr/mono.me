using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Validates AuthKit user access tokens: issuer Auth:AuthKit:Issuer, JWKS Auth:AuthKit:JwksUri,
// audience Auth:AuthKit:Audience when set. M2M tokens are AuthKitMachineTokenValidator's.
public class AuthKitBearerTokenValidator : IBearerTokenValidator
{
    private const string UserIdPrefix = "user_";

    private readonly AuthKitTokenSource _source;
    private readonly AuthKitClaimNormalizer _normalizer;

    public AuthKitBearerTokenValidator(
        IOptions<UserAuthenticationOptions> options,
        AuthKitClaimNormalizer normalizer,
        IHostEnvironment environment)
        : this(options, normalizer, environment, AuthKitTokenSource.CreateKeyManager(options.Value.AuthKit.GetJwksUri()))
    {
    }

    public AuthKitBearerTokenValidator(
        IOptions<UserAuthenticationOptions> options,
        AuthKitClaimNormalizer normalizer,
        IHostEnvironment environment,
        IConfigurationManager<JsonWebKeySet> keyManager)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(normalizer);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(keyManager);

        var authKit = options.Value.AuthKit;

        if (string.IsNullOrWhiteSpace(authKit.Issuer))
        {
            throw new InvalidOperationException("Auth:AuthKit:Issuer is required for AuthKit token validation.");
        }

        _source = new AuthKitTokenSource(
            authKit.Issuer,
            string.IsNullOrWhiteSpace(authKit.Audience) ? null : authKit.Audience,
            keyManager);
        _normalizer = normalizer;

        if (environment.IsDevelopment())
        {
            // Process-wide. Production leaves the default (false) so token details stay out of logs.
            IdentityModelEventSource.ShowPII = true;
        }
    }

    public async Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        // A token from another issuer (an M2M token from the AuthKit domain, for example) is not
        // this validator's; no keys are fetched for it.
        if (!AuthKitTokenSource.IssuerMatches(AuthKitTokenSource.TryReadIssuer(rawToken), _source.Issuer))
        {
            return null;
        }

        var claims = await _source.ValidateAsync(rawToken, cancellationToken);
        var subject = claims?.FirstOrDefault(claim => claim.Type == "sub")?.Value;

        // User tokens carry a WorkOS user id. Anything else must not be treated as a user; with a
        // custom auth domain an M2M token can share this issuer and is left to the machine validator.
        return claims is not null && subject is not null && subject.StartsWith(UserIdPrefix, StringComparison.Ordinal)
            ? _normalizer.NormalizeUser(claims)
            : null;
    }
}
