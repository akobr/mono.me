namespace _42.Platform.Storyteller;

// A domain error that the API answers with a client status code and a stable error code.
// Services throw the derived types; the HTTP layer maps them, without exception details.
public abstract class StorytellerException : Exception
{
    protected StorytellerException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    protected StorytellerException(string message, string errorCode, Exception? innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
