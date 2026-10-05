using System.Net;

namespace _42.Platform.Storyteller;

// A failed WorkOS management call. The message carries the status and the WorkOS error text, never the key.
public sealed class WorkOsApiException : HttpRequestException
{
    public WorkOsApiException(HttpStatusCode statusCode, string message)
        : base(message, null, statusCode)
    {
    }
}
