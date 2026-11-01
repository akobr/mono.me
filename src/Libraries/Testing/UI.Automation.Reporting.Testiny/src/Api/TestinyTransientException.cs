using System.Net;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// A Testiny call that may succeed later: 429 (rate limit), 408, 5xx, a timeout or a network failure. The runtime's
// dispatcher retries it with back-off, at least RetryAfter when Testiny named a delay.
public sealed class TestinyTransientException : TransientReporterException
{
    public TestinyTransientException(string message, HttpStatusCode? statusCode = null, TimeSpan? retryAfter = null, Exception? innerException = null)
        : base(message, innerException, retryAfter)
    {
        StatusCode = statusCode;
    }

    // Null for a timeout or a network failure.
    public HttpStatusCode? StatusCode { get; }

    // True when Testiny may have carried the request out although the answer was lost: a timeout, a network failure
    // after the connection was made, or a 5xx other than 503. 429, 408 and 503 were refused before the work, and a
    // connection that never opened sent nothing, so a retry of those cannot repeat anything.
    public bool MayHaveBeenApplied => StatusCode switch
    {
        null => InnerException is not HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError },
        HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout or HttpStatusCode.ServiceUnavailable => false,
        var status => (int)status >= 500,
    };
}
