namespace _42.Platform.Storyteller.Mcp.Authentication;

// The exchange did not answer: the API is temporarily unavailable (503), or the call failed or timed out on the way.
public sealed class StorytellerSignInUnavailableException : StorytellerSignInException
{
    public const string ErrorCodeValue = "temporarily_unavailable";

    public const string UnavailableMessage =
        "Sign-in to the Storyteller API failed: its token exchange is temporarily unavailable. Try again in a moment.";

    public StorytellerSignInUnavailableException()
        : base(UnavailableMessage, ErrorCodeValue)
    {
    }
}
