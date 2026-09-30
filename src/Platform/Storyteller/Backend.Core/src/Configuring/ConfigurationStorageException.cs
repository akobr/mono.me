using System.Net;

namespace _42.Platform.Storyteller.Configuring;

/// <summary>
/// An unexpected failure of the configuration storage (not caused by the input of the request).
/// </summary>
public class ConfigurationStorageException(string message, HttpStatusCode statusCode)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
