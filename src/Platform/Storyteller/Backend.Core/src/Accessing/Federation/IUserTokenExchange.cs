namespace _42.Platform.Storyteller.Accessing.Federation;

// The RFC 8693 exchange behind POST v1/access/token: a user token of the deployment's identity provider, presented by an
// actor of Auth:TokenExchange:Actors (the MCP server) with its proof, for a short-lived Storyteller user token that names
// the person (sub) and the actor (act). It never creates accounts.
public interface IUserTokenExchange
{
    // A refused request is a result with the OAuth error, never an exception.
    Task<UserTokenExchangeResult> ExchangeAsync(UserTokenExchangeRequest request, CancellationToken cancellationToken = default);
}
