namespace _42.Platform.Storyteller.Mcp.Authentication;

// A sign-in problem the person or an administrator has to solve. The message is fixed text: never a token, never MSAL's
// own message.
public class StorytellerSignInException : Exception
{
    public StorytellerSignInException(string message, string? errorCode = null)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string? ErrorCode { get; }
}
