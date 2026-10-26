namespace _42.Platform.Storyteller.Accessing.Federation;

// The outcome of a user token exchange, mapped by the endpoint to 200, 400 (an OAuth error) or 503 (unavailable).
// Every refusal has one fixed description; the reason goes to the logs.
public sealed record UserTokenExchangeResult
{
    // unsupported_grant_type, while Auth:TokenExchange:Enabled is off.
    public const string DisabledDescription = "The exchange of user tokens is not enabled.";

    // invalid_client: one text for an unknown actor and a bad proof, so the endpoint cannot be used to probe actors.
    public const string ActorNotAcceptedDescription = "The actor is not known or its proof is not valid.";

    // invalid_grant: the subject token is not a valid user token of the deployment's provider for this actor, or it
    // reached Auth:TokenExchange:MaxExchangesPerSubjectTokenPerMinute.
    public const string SubjectNotAcceptedDescription = "The subject token is not accepted for this actor.";

    // invalid_grant: the exchange never creates accounts.
    public const string NoAccountDescription = "No Storyteller account exists for this sign-in. Register once with sform or the admin UI.";

    // invalid_scope: the granted scope is the requested one within the actor's, and it acts for the person.
    public const string ScopeNotGrantedDescription = "The requested scope is not granted to the actor; it must include User.Impersonation.";

    public const string KeysUnavailableDescription = "The keys to verify the request could not be retrieved.";

    public const string SigningKeyUnavailableDescription = "The signing key of Storyteller tokens is not available.";

    private UserTokenExchangeResult()
    {
    }

    public bool Succeeded => Error is null;

    public string? AccessToken { get; private init; }

    // Always TokenTypes.AccessToken.
    public string? IssuedTokenType { get; private init; }

    // Seconds until the access token expires.
    public int ExpiresIn { get; private init; }

    // The granted scopes, space separated.
    public string? Scope { get; private init; }

    // The actor's restrictions (ActorRestrictions), which the token carries too; a non-standard member of the answer,
    // so the actor can adapt (the MCP server hides its secret tools under NoSecretReveal).
    public IReadOnlyList<string> Restrictions { get; private init; } = [];

    // An OAuthTokenExchange.Errors value.
    public string? Error { get; private init; }

    public string? ErrorDescription { get; private init; }

    // 503 temporarily_unavailable with Retry-After: keys could not be retrieved; the request may succeed later.
    public bool IsUnavailable { get; private init; }

    public static UserTokenExchangeResult Success(string accessToken, int expiresIn, string scope, IReadOnlyList<string> restrictions)
    {
        ArgumentException.ThrowIfNullOrEmpty(accessToken);
        ArgumentNullException.ThrowIfNull(restrictions);

        return new UserTokenExchangeResult
        {
            AccessToken = accessToken,
            IssuedTokenType = OAuthTokenExchange.TokenTypes.AccessToken,
            ExpiresIn = expiresIn,
            Scope = scope,
            Restrictions = restrictions,
        };
    }

    public static UserTokenExchangeResult Failure(string error, string description)
    {
        return new UserTokenExchangeResult { Error = error, ErrorDescription = description };
    }

    public static UserTokenExchangeResult Unavailable(string description = KeysUnavailableDescription)
    {
        return new UserTokenExchangeResult
        {
            Error = OAuthTokenExchange.Errors.TemporarilyUnavailable,
            ErrorDescription = description,
            IsUnavailable = true,
        };
    }
}
