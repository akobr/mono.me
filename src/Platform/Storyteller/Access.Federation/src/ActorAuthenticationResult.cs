using System.Diagnostics.CodeAnalysis;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;

namespace _42.Platform.Storyteller;

// The outcome of ActorAuthenticator: the actor whose proof is valid, or the OAuth error to answer with and why.
public sealed record ActorAuthenticationResult
{
    private ActorAuthenticationResult()
    {
    }

    [MemberNotNullWhen(true, nameof(Actor))]
    [MemberNotNullWhen(false, nameof(Error), nameof(ErrorDescription), nameof(RefusalReason))]
    public bool Succeeded => Actor is not null;

    public TokenExchangeActorOptions? Actor { get; private init; }

    // invalid_client, or invalid_request for a request whose proof is malformed (two proofs, a type without its token).
    public string? Error { get; private init; }

    public string? ErrorDescription { get; private init; }

    // For the logs only; it never contains a token or the secret.
    public string? RefusalReason { get; private init; }

    public static ActorAuthenticationResult Authenticated(TokenExchangeActorOptions actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return new ActorAuthenticationResult { Actor = actor };
    }

    // An unknown actor and a bad proof answer alike.
    public static ActorAuthenticationResult NotAccepted(string reason)
    {
        return new ActorAuthenticationResult
        {
            Error = OAuthTokenExchange.Errors.InvalidClient,
            ErrorDescription = UserTokenExchangeResult.ActorNotAcceptedDescription,
            RefusalReason = reason,
        };
    }

    public static ActorAuthenticationResult Malformed(string description)
    {
        return new ActorAuthenticationResult
        {
            Error = OAuthTokenExchange.Errors.InvalidRequest,
            ErrorDescription = description,
            RefusalReason = description,
        };
    }
}
