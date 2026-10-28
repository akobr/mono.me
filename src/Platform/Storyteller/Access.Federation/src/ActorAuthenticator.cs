using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Authenticates the actor of a user token exchange: the entry of Auth:TokenExchange:Actors that client_id names, by the
// one proof its settings name. A workload identity token (actor_token), an RFC 7523 client assertion (private_key_jwt),
// or in Development only a client secret (client_secret_post). Keys come from the actor's settings through
// FederatedIssuerKeyResolver (its SSRF-safe client and size limits), never from a URL a token names, and identity claims
// are read only once the signature is verified.
public sealed class ActorAuthenticator : IDisposable
{
    public const string ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";

    // RFC 7523 section 3: a client assertion lives a short time and is used once. One minute keeps the window short in
    // which an assertion recorded on one instance could be replayed on another.
    public const int MaxClientAssertionLifetimeSeconds = 60;

    // The deployment scope the actors' keys are cached under, with the actor id as the project. No organization name
    // has a '~', so it never meets a project's trusted issuers.
    internal const string KeyScope = "~token-exchange";

    // Client assertions recorded at once, each until it expires; more are refused.
    internal const int MaxRecordedAssertions = 50_000;

    private const string WorkloadIdentityIssuerName = "workload-identity";
    private const string ClientAssertionIssuerName = "client-assertion";

    private static readonly string[] ActorTokenTypes =
    [
        OAuthTokenExchange.TokenTypes.Jwt,
        OAuthTokenExchange.TokenTypes.AccessToken,
    ];

    private readonly IssuerTokenVerifier _tokenVerifier;
    private readonly IOptions<TokenExchangeOptions> _exchangeOptions;
    private readonly IOptions<FederationOptions> _options;
    private readonly IHostEnvironment _environment;
    private readonly ReplayRecord _assertions;

    public ActorAuthenticator(
        FederatedIssuerKeyResolver keyResolver,
        IOptions<TokenExchangeOptions> exchangeOptions,
        IOptions<FederationOptions> options,
        IHostEnvironment environment,
        TimeProvider timeProvider)
    {
        _tokenVerifier = new IssuerTokenVerifier(keyResolver, timeProvider);
        _exchangeOptions = exchangeOptions;
        _options = options;
        _environment = environment;
        _assertions = new ReplayRecord(MaxRecordedAssertions, timeProvider);
    }

    // The actor, or the error with the reason for the logs. Throws BearerKeyRetrievalException when the actor's keys
    // cannot be retrieved.
    public async Task<ActorAuthenticationResult> AuthenticateAsync(UserTokenExchangeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. The form: client_id and exactly one proof with its type.
        if (CheckForm(request) is { } refused)
        {
            return refused;
        }

        // 2. The actor, compared as configured: client ids are case sensitive.
        var actor = _exchangeOptions.Value.Actors.FirstOrDefault(candidate => string.Equals(candidate.Id, request.ClientId, StringComparison.Ordinal));

        if (actor is null)
        {
            return ActorAuthenticationResult.NotAccepted("the client_id names no actor");
        }

        // 3. The proof of the method its settings name; another one is refused, even a valid one.
        var authentication = actor.Authentication ?? new ActorAuthenticationOptions();
        var methods = (authentication.WorkloadIdentity is null ? 0 : 1)
            + (authentication.ClientAssertion is null ? 0 : 1)
            + (string.IsNullOrWhiteSpace(authentication.ClientSecretHash) ? 0 : 1);

        if (methods != 1)
        {
            return ActorAuthenticationResult.NotAccepted("the actor's settings do not name exactly one proof method");
        }

        if (authentication.WorkloadIdentity is { } workloadIdentity)
        {
            return request.ActorToken is { Length: > 0 } actorToken
                ? await VerifyWorkloadIdentityAsync(actor, workloadIdentity, actorToken, cancellationToken)
                : ActorAuthenticationResult.NotAccepted($"the actor proves itself with WorkloadIdentity, but the request carries {DescribeProof(request)}");
        }

        if (authentication.ClientAssertion is { } clientAssertion)
        {
            return request.ClientAssertion is { Length: > 0 } assertion
                ? await VerifyClientAssertionAsync(actor, clientAssertion, assertion, request.TokenEndpoint, cancellationToken)
                : ActorAuthenticationResult.NotAccepted($"the actor proves itself with ClientAssertion, but the request carries {DescribeProof(request)}");
        }

        return request.ClientSecret is { Length: > 0 } secret
            ? VerifyClientSecret(actor, authentication.ClientSecretHash!, secret)
            : ActorAuthenticationResult.NotAccepted($"the actor proves itself with ClientSecretHash, but the request carries {DescribeProof(request)}");
    }

    public void Dispose()
    {
        _assertions.Dispose();
    }

    private static ActorAuthenticationResult? CheckForm(UserTokenExchangeRequest request)
    {
        if (string.IsNullOrEmpty(request.ClientId))
        {
            return ActorAuthenticationResult.Malformed("The parameter client_id is missing.");
        }

        var hasActorToken = !string.IsNullOrEmpty(request.ActorToken);
        var hasClientAssertion = !string.IsNullOrEmpty(request.ClientAssertion);
        var hasClientSecret = !string.IsNullOrEmpty(request.ClientSecret);

        // RFC 8693 section 2.1: actor_token_type comes with actor_token and only with it; RFC 7521 likewise.
        if (hasActorToken != !string.IsNullOrEmpty(request.ActorTokenType))
        {
            return ActorAuthenticationResult.Malformed(hasActorToken ? "The parameter actor_token_type is missing." : "The parameter actor_token is missing.");
        }

        if (hasClientAssertion != !string.IsNullOrEmpty(request.ClientAssertionType))
        {
            return ActorAuthenticationResult.Malformed(hasClientAssertion ? "The parameter client_assertion_type is missing." : "The parameter client_assertion is missing.");
        }

        // RFC 6749 section 2.3: a client uses one authentication method per request.
        if ((hasActorToken ? 1 : 0) + (hasClientAssertion ? 1 : 0) + (hasClientSecret ? 1 : 0) > 1)
        {
            return ActorAuthenticationResult.Malformed("The request must carry one actor proof: actor_token, client_assertion or client_secret.");
        }

        if (hasActorToken && !ActorTokenTypes.Contains(request.ActorTokenType, StringComparer.Ordinal))
        {
            return ActorAuthenticationResult.Malformed("The actor token type is not supported.");
        }

        if (hasClientAssertion && !string.Equals(request.ClientAssertionType, ClientAssertionType, StringComparison.Ordinal))
        {
            return ActorAuthenticationResult.Malformed("The client assertion type is not supported.");
        }

        return hasActorToken || hasClientAssertion || hasClientSecret
            ? null
            : ActorAuthenticationResult.NotAccepted("the request carries no actor proof");
    }

    private static string DescribeProof(UserTokenExchangeRequest request)
    {
        return !string.IsNullOrEmpty(request.ActorToken) ? "an actor_token"
            : !string.IsNullOrEmpty(request.ClientAssertion) ? "a client_assertion"
            : "a client_secret";
    }

    // Fixed-size keys, whatever the length of the jti.
    private static string GetAssertionKey(string actorId, string tokenId)
    {
        return $"{actorId}\n{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenId)))}";
    }

    // A token of the actor's platform identity: iss and sub as configured, Audience among its aud. Managed identity
    // tokens live about a day, so only exp limits the lifetime.
    private async Task<ActorAuthenticationResult> VerifyWorkloadIdentityAsync(
        TokenExchangeActorOptions actor,
        ActorWorkloadIdentityOptions identity,
        string actorToken,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;

        if (!UnverifiedJwt.TryRead(actorToken, options.MaxSubjectTokenBytes, out var token, out var formError))
        {
            return ActorAuthenticationResult.NotAccepted($"the actor token is refused: {formError}");
        }

        // No keys are fetched for a token of another issuer.
        if (!string.Equals(token.Issuer, identity.Issuer, StringComparison.Ordinal))
        {
            return ActorAuthenticationResult.NotAccepted("the actor token's iss is not the actor's WorkloadIdentity:Issuer");
        }

        var issuer = new FederatedIssuer
        {
            Name = WorkloadIdentityIssuerName,
            Issuer = identity.Issuer,
            KeySource = string.IsNullOrWhiteSpace(identity.JwksUri) ? FederatedIssuerKeySource.Discovery : FederatedIssuerKeySource.JwksUri,
            JwksUri = string.IsNullOrWhiteSpace(identity.JwksUri) ? null : identity.JwksUri,
        };
        var policy = new IssuerTokenPolicy([identity.Audience], TimeSpan.FromSeconds(options.ClockSkewSeconds));
        var verification = await _tokenVerifier.VerifyAsync(actorToken, token, KeyScope, actor.Id, issuer, policy, cancellationToken);

        if (!verification.IsValid)
        {
            return ActorAuthenticationResult.NotAccepted($"the actor token is refused: {verification.RefusalReason}");
        }

        return string.Equals(IssuerTokenVerifier.GetString(token.Payload, "sub"), identity.Subject, StringComparison.Ordinal)
            ? ActorAuthenticationResult.Authenticated(actor)
            : ActorAuthenticationResult.NotAccepted("the actor token's sub is not the actor's WorkloadIdentity:Subject");
    }

    // RFC 7523 section 3: iss and sub are the actor id, aud names this token endpoint or the Storyteller issuer, exp
    // within one minute, and a jti that was not used before. The keys are only those of ClientAssertion:JwksUri.
    private async Task<ActorAuthenticationResult> VerifyClientAssertionAsync(
        TokenExchangeActorOptions actor,
        ActorClientAssertionOptions settings,
        string assertion,
        string? tokenEndpoint,
        CancellationToken cancellationToken)
    {
        var options = _options.Value;

        if (!UnverifiedJwt.TryRead(assertion, options.MaxSubjectTokenBytes, out var token, out var formError))
        {
            return ActorAuthenticationResult.NotAccepted($"the client assertion is refused: {formError}");
        }

        if (!string.Equals(token.Issuer, actor.Id, StringComparison.Ordinal))
        {
            return ActorAuthenticationResult.NotAccepted("the client assertion's iss is not the actor id");
        }

        var issuer = new FederatedIssuer
        {
            Name = ClientAssertionIssuerName,
            Issuer = actor.Id,
            KeySource = FederatedIssuerKeySource.JwksUri,
            JwksUri = settings.JwksUri,
        };
        var audiences = new[] { tokenEndpoint, options.Issuer }
            .Where(audience => !string.IsNullOrWhiteSpace(audience))
            .Select(audience => audience!)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var clockSkew = TimeSpan.FromSeconds(options.ClockSkewSeconds);
        var policy = new IssuerTokenPolicy(audiences, clockSkew, MaxLifetimeSeconds: MaxClientAssertionLifetimeSeconds);
        var verification = await _tokenVerifier.VerifyAsync(assertion, token, KeyScope, actor.Id, issuer, policy, cancellationToken);

        if (!verification.IsValid)
        {
            return ActorAuthenticationResult.NotAccepted($"the client assertion is refused: {verification.RefusalReason}");
        }

        if (!string.Equals(IssuerTokenVerifier.GetString(token.Payload, "sub"), actor.Id, StringComparison.Ordinal))
        {
            return ActorAuthenticationResult.NotAccepted("the client assertion's sub is not the actor id");
        }

        if (IssuerTokenVerifier.GetString(token.Payload, "jti") is not { } tokenId)
        {
            return ActorAuthenticationResult.NotAccepted("the client assertion has no jti");
        }

        // Kept as long as the assertion is accepted: until exp, with the skew. The policy read exp as a number.
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds((long)token.Payload.GetProperty("exp").GetDouble()) + clockSkew;

        if (!_assertions.TryAdd(GetAssertionKey(actor.Id, tokenId), expiresAt))
        {
            return ActorAuthenticationResult.NotAccepted(_assertions.IsFull
                ? "too many client assertions are recorded to check its jti"
                : "the client assertion's jti was used before");
        }

        return ActorAuthenticationResult.Authenticated(actor);
    }

    // The hex SHA-256 hash of the secret, compared in constant time. Development only, also when the settings slipped
    // past the check at start.
    private ActorAuthenticationResult VerifyClientSecret(TokenExchangeActorOptions actor, string secretHash, string secret)
    {
        if (!_environment.IsDevelopment())
        {
            return ActorAuthenticationResult.NotAccepted("a client secret is accepted in the Development environment only");
        }

        byte[] expected;

        try
        {
            expected = Convert.FromHexString(secretHash.Trim());
        }
        catch (FormatException)
        {
            return ActorAuthenticationResult.NotAccepted("the actor's ClientSecretHash is not a hex SHA-256 hash");
        }

        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

        return CryptographicOperations.FixedTimeEquals(actual, expected)
            ? ActorAuthenticationResult.Authenticated(actor)
            : ActorAuthenticationResult.NotAccepted("the client secret is not the actor's");
    }
}
