namespace _42.Platform.Storyteller.Mcp.Authentication;

// The MCP server cannot act for anyone: the API does not accept it as an actor, has the exchange turned off, or the
// server cannot produce its own credential. An administrator solves it; the texts name the settings.
public sealed class StorytellerActorException : StorytellerSignInException
{
    public const string ActorNotAcceptedMessage =
        "The Storyteller API does not accept the MCP server as an actor of its token exchange. An administrator checks that "
        + "the API's Auth:TokenExchange:Actors lists Mcp:Actor:Id with the credential the MCP server uses (Mcp:Actor). "
        + "With Mcp:Actor:ClientAssertionKey, the assertion's aud ({Storyteller:BaseUrl}/v1/access/token, or "
        + "Mcp:Actor:ClientAssertionKey:Audience) must be the API's {MachineAuth:Federation:PublicBaseUrl}/v1/access/token "
        + "or its MachineAuth:Federation:Issuer.";

    public const string ExchangeDisabledMessage =
        "The Storyteller API has the user token exchange turned off (Auth:TokenExchange:Enabled), so the MCP server cannot "
        + "act for anyone.";

    public const string CredentialUnavailableMessage =
        "The MCP server could not produce the credential of its actor (Mcp:Actor): its workload identity token or its client "
        + "assertion key is not available.";

    public StorytellerActorException(string message, string errorCode)
        : base(message, errorCode)
    {
    }
}
