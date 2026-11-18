using _42.Platform.Storyteller.Accessing;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Logging;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller;

// Validates AuthKit user access tokens: issuer Auth:AuthKit:Issuer, JWKS Auth:AuthKit:JwksUri, and one of the audiences
// Auth:AuthKit:Audiences (or the single Auth:AuthKit:Audience, AuthKitOptions.GetAudiences) when any are set. Every WorkOS
// application of the environment has the same issuer, so the audiences keep other products' tokens out. M2M tokens are
// AuthKitMachineTokenValidator's.
public class AuthKitBearerTokenValidator : IBearerTokenValidator
{
    private const string UserIdPrefix = "user_";

    private readonly AuthKitTokenSource _source;
    private readonly AuthKitClaimNormalizer _normalizer;
    private readonly ILogger<AuthKitBearerTokenValidator> _logger;

    public AuthKitBearerTokenValidator(
        IOptions<UserAuthenticationOptions> options,
        AuthKitClaimNormalizer normalizer,
        IHostEnvironment environment,
        ILogger<AuthKitBearerTokenValidator> logger)
        : this(options, normalizer, environment, logger, AuthKitTokenSource.CreateKeyManager(options.Value.AuthKit.GetJwksUri()))
    {
    }

    public AuthKitBearerTokenValidator(
        IOptions<UserAuthenticationOptions> options,
        AuthKitClaimNormalizer normalizer,
        IHostEnvironment environment,
        ILogger<AuthKitBearerTokenValidator> logger,
        IConfigurationManager<JsonWebKeySet> keyManager)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(normalizer);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(keyManager);

        var authKit = options.Value.AuthKit;

        if (string.IsNullOrWhiteSpace(authKit.Issuer))
        {
            throw new InvalidOperationException("Auth:AuthKit:Issuer is required for AuthKit token validation.");
        }

        _source = new AuthKitTokenSource(authKit.Issuer, authKit.GetAudiences(), keyManager);
        _normalizer = normalizer;
        _logger = logger;

        // The single Audience is the JWT template's, which every WorkOS application of the environment gets. Listed next to
        // the 2S audiences it is the transition of the rollout; left there, it lets other products' tokens in.
        if (authKit.ListsTheSingleAudience())
        {
            _logger.LogWarning(
                "Auth:AuthKit:Audiences lists the single Auth:AuthKit:Audience {Audience}: a token of any WorkOS application that carries it is accepted. Remove it from the list once Multiple Applications is on.",
                AuthKitTokenSource.Sanitize(authKit.Audience!.Trim()));
        }

        if (environment.IsDevelopment())
        {
            // Process-wide. Production leaves the default (false) so token details stay out of logs.
            IdentityModelEventSource.ShowPII = true;
        }
    }

    // Auth:AuthKit:Issuer with its key set, shared with AuthKitUserSubjectTokenValidator.
    internal AuthKitTokenSource Source => _source;

    public async Task<BearerValidationResult?> ValidateAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        // A token from another issuer (an M2M token from the AuthKit domain, for example) is not
        // this validator's; no keys are fetched for it. The Auth:TokenExchange:AdditionalIssuers are such issuers:
        // their tokens serve the exchange only.
        if (!AuthKitTokenSource.IssuerMatches(AuthKitTokenSource.TryReadIssuer(rawToken), _source.Issuer))
        {
            return null;
        }

        // A token that does not pass names nobody, so its refusal is not logged here.
        var result = await _source.ValidateTokenAsync(rawToken, cancellationToken);

        if (!result.IsValid)
        {
            return null;
        }

        var claims = result.ClaimsIdentity.Claims.ToList();
        var subject = claims.FirstOrDefault(claim => claim.Type == "sub")?.Value;

        // User tokens carry a WorkOS user id. Anything else must not be treated as a user; with a
        // custom auth domain an M2M token can share this issuer and is left to the machine validator.
        if (subject is null || !subject.StartsWith(UserIdPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        // WorkOS impersonation: a dashboard member signed in as the user, and act.sub is their email. Refused: 2S does
        // not support impersonated sessions, and accepting one would serve and record the user, not the impersonator.
        // Supporting it is a separate decision: act.sub as the audit's Via, no secret read or write for the session,
        // and a log of every request. No machine validator takes a user_ token, so the request gets 401.
        if (AuthKitTokenSource.HasActor(result.SecurityToken))
        {
            _logger.LogWarning(
                "An AuthKit user token of {Subject} is refused: it names an actor (act.sub {Actor}), a WorkOS impersonation",
                AuthKitTokenSource.Sanitize(subject),
                AuthKitTokenSource.DescribeActor(result.SecurityToken));
            return null;
        }

        return _normalizer.NormalizeUser(claims);
    }
}
