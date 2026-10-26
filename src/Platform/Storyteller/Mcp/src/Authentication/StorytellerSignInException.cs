namespace _42.Platform.Storyteller.Mcp.Authentication;

// A sign-in problem the person or an administrator has to solve. The message is fixed text (or one of the API's fixed
// error descriptions): never a token, never a response body.
public class StorytellerSignInException : Exception
{
    public StorytellerSignInException(string message, string? errorCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}
