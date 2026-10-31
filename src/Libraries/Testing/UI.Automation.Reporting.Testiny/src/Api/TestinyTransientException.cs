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
}
