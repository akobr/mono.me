namespace _42.Platform.Storyteller;

// A dependency the operation needs is throttling or down. The API answers it with 503 and Retry-After.
public sealed class ServiceUnavailableException(string message, string errorCode, TimeSpan? retryAfter = null, Exception? innerException = null)
    : StorytellerException(message, errorCode, innerException)
{
    public TimeSpan? RetryAfter { get; } = retryAfter;
}
