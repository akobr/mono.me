namespace _42.Platform.Storyteller.Mcp.Authentication;

// Entra wants an interaction the MCP server cannot pass on: consent, or a claims challenge (Conditional Access, MFA).
public sealed class StorytellerSignInRequiredException : StorytellerSignInException
{
    public const string ConsentMessage =
        "The Storyteller API has not been granted to the MCP app for you. An administrator has to grant consent "
        + "(or pre-authorize the MCP app on the Storyteller API registration).";

    public const string ClaimsChallengeMessage =
        "The Storyteller API asks for an extra sign-in step that the MCP server cannot pass on. An administrator has to "
        + "apply the same Conditional Access policy to the MCP app.";

    public StorytellerSignInRequiredException(string message, string? errorCode)
        : base(message, errorCode)
    {
    }
}
