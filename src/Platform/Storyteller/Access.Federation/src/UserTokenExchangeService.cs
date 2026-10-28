using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// The RFC 8693 exchange of a person's token for a Storyteller user token, by an actor of Auth:TokenExchange:Actors.
// The checks run in the order of the spec, and only an authenticated actor gets the subject token validated: the form,
// the actor and its proof, the subject token (the provider's rules, the actor's audiences and clients), the account, the
// replay limit, then the scope. Separate from FederatedTokenExchangeService, which exchanges machine tokens.
public sealed class UserTokenExchangeService : IUserTokenExchange, IDisposable
{
    // Subject tokens recorded at once for the replay limit, each for a minute; more are refused.
    internal const int MaxRecordedSubjectTokens = 50_000;

    // A subject token that expires sooner gets no token: it would not outlive the call that uses it.
    internal static readonly TimeSpan MinSubjectTokenLifetime = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan ReplayWindow = TimeSpan.FromMinutes(1);

    private static readonly string[] SubjectTokenTypes =
    [
        OAuthTokenExchange.TokenTypes.AccessToken,
        OAuthTokenExchange.TokenTypes.Jwt,
    ];

    private readonly ActorAuthenticator _actorAuthenticator;
    private readonly IUserSubjectTokenValidator _subjectTokenValidator;
    private readonly IAccessService _accessService;
    private readonly StorytellerTokenIssuer _tokenIssuer;
    private readonly IOptions<TokenExchangeOptions> _exchangeOptions;
    private readonly IOptions<FederationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UserTokenExchangeService> _logger;
    private readonly ReplayRecord _exchanges;

    public UserTokenExchangeService(
        ActorAuthenticator actorAuthenticator,
        IUserSubjectTokenValidator subjectTokenValidator,
        IAccessService accessService,
        StorytellerTokenIssuer tokenIssuer,
        IOptions<TokenExchangeOptions> exchangeOptions,
        IOptions<FederationOptions> options,
        TimeProvider timeProvider,
        ILogger<UserTokenExchangeService> logger)
    {
        _actorAuthenticator = actorAuthenticator;
        _subjectTokenValidator = subjectTokenValidator;
        _accessService = accessService;
        _tokenIssuer = tokenIssuer;
        _exchangeOptions = exchangeOptions;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _exchanges = new ReplayRecord(MaxRecordedSubjectTokens, timeProvider);
    }

    public async Task<UserTokenExchangeResult> ExchangeAsync(UserTokenExchangeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var exchangeOptions = _exchangeOptions.Value;

        // 0. Off: the actors are not trusted, and no grant is.
        if (!exchangeOptions.Enabled)
        {
            _logger.LogDebug("A user token exchange is refused: Auth:TokenExchange:Enabled is off");
            return UserTokenExchangeResult.Failure(OAuthTokenExchange.Errors.UnsupportedGrantType, UserTokenExchangeResult.DisabledDescription);
        }

        // 1. The form, before anything is verified.
        if (ValidateRequest(request, _options.Value) is { } invalid)
        {
            _logger.LogInformation("A user token exchange is refused: {Reason}", invalid.ErrorDescription);
            return invalid;
        }

        // 2. The actor and its proof.
        ActorAuthenticationResult authentication;

        try
        {
            authentication = await _actorAuthenticator.AuthenticateAsync(request, cancellationToken);
        }
        catch (BearerKeyRetrievalException exception)
        {
            _logger.LogWarning(exception, "A user token exchange by the actor {ActorId} is unavailable: its keys could not be retrieved", ForLog(request.ClientId));
            return UserTokenExchangeResult.Unavailable();
        }

        if (!authentication.Succeeded)
        {
            _logger.LogInformation(
                "A user token exchange by the actor {ActorId} is refused: {Reason}",
                ForLog(request.ClientId),
                authentication.RefusalReason);
            return UserTokenExchangeResult.Failure(authentication.Error, authentication.ErrorDescription);
        }

        var actor = authentication.Actor;

        // 3. The subject token: a user token of the deployment's provider, issued for this actor.
        var subject = await ValidateSubjectTokenAsync(request.SubjectToken!, actor, cancellationToken);

        if (subject is null)
        {
            return UserTokenExchangeResult.Unavailable();
        }

        if (!subject.IsAccepted)
        {
            return RefuseSubject(actor, subject.RefusalReason);
        }

        var now = _timeProvider.GetUtcNow();

        if (subject.ExpiresAt - now < MinSubjectTokenLifetime)
        {
            return RefuseSubject(actor, $"the subject token expires within {MinSubjectTokenLifetime.TotalSeconds} seconds");
        }

        // 4. The account, which the exchange never creates. Before the replay limit, so a sign-in without an account
        // always gets the registration hint; it is not counted, as no token is issued.
        var account = await _accessService.GetAccountAsync(subject.AccountId);

        if (account is null)
        {
            _logger.LogInformation(
                "A user token exchange by the actor {ActorId} is refused: no account {AccountId} exists",
                actor.Id,
                subject.AccountId);
            return UserTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.NoAccountDescription);
        }

        // 5. The replay limit, per actor and subject token. The client gets the generic text of a refused subject token;
        // the reason is only logged.
        if (!_exchanges.TryCount(GetReplayKey(actor.Id, subject.TokenId, request.SubjectToken!), ReplayWindow, exchangeOptions.MaxExchangesPerSubjectTokenPerMinute))
        {
            if (_exchanges.IsFull)
            {
                _logger.LogWarning("A user token exchange by the actor {ActorId} is refused: too many subject tokens are recorded for the replay limit", actor.Id);
            }
            else
            {
                _logger.LogInformation(
                    "A user token exchange by the actor {ActorId} is refused: the subject token was exchanged {Max} times within a minute",
                    actor.Id,
                    exchangeOptions.MaxExchangesPerSubjectTokenPerMinute);
            }

            return UserTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.SubjectNotAcceptedDescription);
        }

        // 6. The scope: the requested one within the actor's, or all of the actor's; it acts for the person.
        var actorScopes = actor.GetScopes();
        var requested = string.IsNullOrEmpty(request.Scope) ? [] : request.Scope.Split(' ').Distinct(StringComparer.Ordinal).ToList();
        var scopes = requested.Count == 0
            ? actorScopes.ToList()
            : requested.Where(scope => actorScopes.Contains(scope, StringComparer.Ordinal)).ToList();

        // Scope names (the syntax check let only scope characters through), never secrets: they tell the operator what an
        // actor asks for beyond its Scopes.
        if (requested.Count > scopes.Count)
        {
            _logger.LogInformation(
                "A user token exchange by the actor {ActorId} drops the requested scopes {DroppedScopes}: they are not in the actor's Scopes",
                actor.Id,
                string.Join(' ', requested.Except(scopes, StringComparer.Ordinal)));
        }

        if (!scopes.Contains(UserScopes.Impersonation, StringComparer.Ordinal))
        {
            _logger.LogInformation(
                "A user token exchange by the actor {ActorId} is refused: the granted scope would not contain {Scope}",
                actor.Id,
                UserScopes.Impersonation);
            return UserTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidScope, UserTokenExchangeResult.ScopeNotGrantedDescription);
        }

        // 7. The Storyteller user token.
        var restrictions = actor.GetRestrictions();
        StorytellerToken issued;

        try
        {
            issued = await _tokenIssuer.IssueUserAsync(new StorytellerUserTokenRequest
            {
                AccountId = subject.AccountId,
                ActorId = actor.Id,
                Scopes = scopes,
                Restrictions = restrictions,
                AuthTime = subject.AuthTime,
                MaxLifetime = TimeSpan.FromMinutes(exchangeOptions.TokenLifetimeMinutes),
                NotAfter = subject.ExpiresAt,
            });
        }
        catch (TokenSigningKeyUnavailableException exception)
        {
            _logger.LogError(exception, "No Storyteller user token can be issued");
            return UserTokenExchangeResult.Unavailable(UserTokenExchangeResult.SigningKeyUnavailableDescription);
        }

        _logger.LogInformation(
            "The actor {ActorId} exchanged a user token of the account {AccountId} (client {ClientId}) for {ExpiresIn} seconds",
            actor.Id,
            subject.AccountId,
            ForLog(subject.ClientId),
            issued.ExpiresIn);

        return UserTokenExchangeResult.Success(issued.AccessToken, issued.ExpiresIn, string.Join(' ', scopes), restrictions);
    }

    public void Dispose()
    {
        _exchanges.Dispose();
    }

    // Null when the request has the shape of a user token exchange, else the refusal.
    private static UserTokenExchangeResult? ValidateRequest(UserTokenExchangeRequest request, FederationOptions options)
    {
        if (string.IsNullOrEmpty(request.GrantType))
        {
            return Invalid("The parameter grant_type is missing.");
        }

        if (!string.Equals(request.GrantType, OAuthTokenExchange.GrantType, StringComparison.Ordinal))
        {
            return UserTokenExchangeResult.Failure(OAuthTokenExchange.Errors.UnsupportedGrantType, "Only the token exchange grant type is supported.");
        }

        if (string.IsNullOrEmpty(request.SubjectToken))
        {
            return Invalid("The parameter subject_token is missing.");
        }

        if (string.IsNullOrEmpty(request.SubjectTokenType))
        {
            return Invalid("The parameter subject_token_type is missing.");
        }

        if (!SubjectTokenTypes.Contains(request.SubjectTokenType, StringComparer.Ordinal))
        {
            return Invalid("The subject token type is not supported.");
        }

        if (!string.IsNullOrEmpty(request.RequestedTokenType)
            && !string.Equals(request.RequestedTokenType, OAuthTokenExchange.TokenTypes.AccessToken, StringComparison.Ordinal))
        {
            return Invalid("The requested token type is not supported.");
        }

        // RFC 8707: the API is the only resource tokens are issued for.
        if ((request.Audiences ?? []).Concat(request.Resources ?? []).Any(target => !string.Equals(target, options.Audience, StringComparison.Ordinal)))
        {
            return UserTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidTarget, "The audience or resource is not this API.");
        }

        // Only the syntax here; what is granted is known once the actor is.
        if (!string.IsNullOrEmpty(request.Scope) && !OAuthTokenExchange.IsScopeSyntax(request.Scope))
        {
            return UserTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidScope, "The parameter scope is malformed.");
        }

        return null;
    }

    private static UserTokenExchangeResult Invalid(string description)
    {
        return UserTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidRequest, description);
    }

    // The token's id (jti, or uti for Entra ID), else the token itself, hashed: fixed-size keys, and no token in memory.
    private static string GetReplayKey(string actorId, string? tokenId, string subjectToken)
    {
        var id = tokenId is null ? $"token:{subjectToken}" : $"id:{tokenId}";
        return $"{actorId}\n{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))}";
    }

    // A value of the request in the logs: no longer than an actor id may be, so a client_id cannot flood them.
    private static string ForLog(string? value)
    {
        return value is null ? "(none)" : value.Length <= 64 ? value : $"{value[..64]}…";
    }

    // Null when the provider's keys cannot be retrieved (503).
    private async Task<UserSubjectTokenResult?> ValidateSubjectTokenAsync(string subjectToken, TokenExchangeActorOptions actor, CancellationToken cancellationToken)
    {
        var maxBytes = _options.Value.MaxSubjectTokenBytes;

        // A UTF-8 byte per character at least, so a long string is refused before it is measured.
        if (subjectToken.Length > maxBytes || Encoding.UTF8.GetByteCount(subjectToken) > maxBytes)
        {
            return UserSubjectTokenResult.Refused($"The subject token has more than {maxBytes} bytes.");
        }

        UserSubjectTokenResult subject;

        try
        {
            subject = await _subjectTokenValidator.ValidateAsync(subjectToken, actor.SubjectAudiences, actor.AllowedClientIds, cancellationToken);
        }
        catch (BearerKeyRetrievalException exception)
        {
            _logger.LogWarning(exception, "A user token exchange by the actor {ActorId} is unavailable: the keys of the identity provider could not be retrieved", actor.Id);
            return null;
        }

        // Each provider refuses a nested act already; checked again, as chained delegation is never issued.
        return subject.IsAccepted && UserSubjectTokenRules.HasActor(subject.Claims)
            ? UserSubjectTokenResult.Refused("The subject token already names an actor (act).")
            : subject;
    }

    private UserTokenExchangeResult RefuseSubject(TokenExchangeActorOptions actor, string reason)
    {
        _logger.LogInformation("A user token exchange by the actor {ActorId} is refused: {Reason}", actor.Id, reason);
        return UserTokenExchangeResult.Failure(OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.SubjectNotAcceptedDescription);
    }
}
