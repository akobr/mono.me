using System.Net;

namespace _42.Testing.UI.Automation.Runtime.Reporting;

// Which reporter failures are worth another attempt: a TransientReporterException, an HTTP 429 or 5xx, a network
// failure without a status, a timeout (also HttpClient's, which surfaces as a cancellation nobody asked for), an I/O
// error.
internal static class TransientFailures
{
    public static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        return exception switch
        {
            TransientReporterException => true,
            HttpRequestException http => http.StatusCode is null or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError,
            TimeoutException => true,
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            IOException => true,
            _ => false,
        };
    }

    public static TimeSpan? RetryAfter(Exception exception)
    {
        return exception is TransientReporterException { RetryAfter: { } delay } ? delay : null;
    }
}
