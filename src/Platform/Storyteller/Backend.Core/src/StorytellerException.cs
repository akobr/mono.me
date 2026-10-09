namespace _42.Platform.Storyteller;

// A domain error that the API answers with a client status code and a stable error code.
// Services throw the derived types; the HTTP layer maps them, without exception details.
public abstract class StorytellerException(string message, string errorCode)
    : Exception(message)
{
    public string ErrorCode { get; } = errorCode;
}
