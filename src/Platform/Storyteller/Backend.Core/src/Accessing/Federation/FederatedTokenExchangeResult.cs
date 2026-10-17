namespace _42.Platform.Storyteller.Accessing.Federation;

// The outcome of a token exchange, mapped by the endpoint to 200, 400 (an OAuth error) or 503 (unavailable).
public sealed record FederatedTokenExchangeResult
{
    // One description for every verification failure, so the endpoint cannot be used to probe projects,
    // machines or rules; the reason goes to the logs.
    public const string NotAcceptedDescription = "The subject token is not accepted for this machine.";

    public const string KeysUnavailableDescription = "The keys of the trusted issuer could not be retrieved.";

    private FederatedTokenExchangeResult()
    {
    }

    public bool Succeeded => Error is null;

    public string? AccessToken { get; private init; }

    public string? IssuedTokenType { get; private init; }

    // Seconds until the access token expires.
    public int ExpiresIn { get; private init; }

    // The granted Storyteller scopes, space separated.
    public string? Scope { get; private init; }

    // An OAuthTokenExchange.Errors value.
    public string? Error { get; private init; }

    public string? ErrorDescription { get; private init; }

    // 503 temporarily_unavailable with Retry-After: keys could not be retrieved; the request may succeed later.
    public bool IsUnavailable { get; private init; }

    public static FederatedTokenExchangeResult Success(string accessToken, string issuedTokenType, int expiresIn, string scope)
    {
        return new FederatedTokenExchangeResult
        {
            AccessToken = accessToken,
            IssuedTokenType = issuedTokenType,
            ExpiresIn = expiresIn,
            Scope = scope,
        };
    }

    public static FederatedTokenExchangeResult Failure(string error, string description)
    {
        return new FederatedTokenExchangeResult { Error = error, ErrorDescription = description };
    }

    // The opaque refusal of RFC 8693 section 2.2.2 for a project, machine or token that does not pass.
    public static FederatedTokenExchangeResult NotAccepted()
    {
        return Failure(OAuthTokenExchange.Errors.InvalidRequest, NotAcceptedDescription);
    }

    public static FederatedTokenExchangeResult Unavailable(string description = KeysUnavailableDescription)
    {
        return new FederatedTokenExchangeResult
        {
            Error = OAuthTokenExchange.Errors.TemporarilyUnavailable,
            ErrorDescription = description,
            IsUnavailable = true,
        };
    }
}
