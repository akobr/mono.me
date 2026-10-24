namespace _42.Platform.Storyteller.Mcp.Authentication;

public sealed class StorytellerSignInFailedException : StorytellerSignInException
{
    public StorytellerSignInFailedException(string? errorCode)
        : base($"The MCP server could not get a token for the Storyteller API ({errorCode ?? "unknown error"}).", errorCode)
    {
    }
}
