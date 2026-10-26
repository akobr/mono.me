namespace _42.Platform.Storyteller.Mcp.Authentication;

// The API refused the person's token for the exchange (invalid_grant). The message is the API's error_description, one
// of its fixed texts: NoAccountMessage for a sign-in without an account, a generic one for a token it does not accept.
public sealed class StorytellerSignInRefusedException : StorytellerSignInException
{
    public const string ErrorCodeValue = "invalid_grant";

    // The API's description of a sign-in without an account (the exchange never creates accounts).
    public const string NoAccountMessage = "No Storyteller account exists for this sign-in. Register once with sform or the admin UI.";

    // When the API names no reason.
    public const string DefaultMessage = "The Storyteller API did not accept the sign-in to the MCP server for its token exchange.";

    public StorytellerSignInRefusedException(string? description)
        : base(string.IsNullOrWhiteSpace(description) ? DefaultMessage : description, ErrorCodeValue)
    {
    }
}
