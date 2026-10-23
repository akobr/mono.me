namespace _42.Platform.Storyteller.Sdk.Configuring;

// A configuration could not be read, validated, applied or switched to. Messages name variables, properties, keys and
// JSON paths, never a secret or a resolved value.
public sealed class StorytellerConfigurationException : Exception
{
    public StorytellerConfigurationException(string message)
        : this(message, null)
    {
    }

    public StorytellerConfigurationException(string message, Exception? innerException)
        : this(message, null, null, null, false, innerException)
    {
    }

    public StorytellerConfigurationException(
        string message,
        string? key,
        int? statusCode,
        string? errorCode,
        bool isTransient,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Key = key;
        StatusCode = statusCode;
        ErrorCode = errorCode;
        IsTransient = isTransient;
    }

    // The full key of the configuration, when the failure belongs to one.
    public string? Key { get; }

    // The HTTP status of the platform's answer; null when no answer arrived or the failure is local.
    public int? StatusCode { get; }

    // The ErrorCode of the platform's ErrorResponse (AccessDenied, binding.evaluation, …), when it sent one.
    public string? ErrorCode { get; }

    // Worth trying again later: the platform, a token endpoint or a vault was unreachable or busy.
    public bool IsTransient { get; }
}
