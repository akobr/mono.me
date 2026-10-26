namespace _42.Platform.Storyteller.Mcp.Authentication;

// Any other refusal of the exchange, or an answer the server cannot read. The error code is the API's OAuth error or a
// fixed name of the server's own (invalid_response, http_{status}).
public sealed class StorytellerSignInFailedException : StorytellerSignInException
{
    public StorytellerSignInFailedException(string? errorCode)
        : base($"The MCP server could not get a token for the Storyteller API ({errorCode ?? "unknown error"}).", errorCode)
    {
    }
}
