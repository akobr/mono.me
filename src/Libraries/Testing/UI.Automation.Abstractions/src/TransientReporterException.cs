namespace _42.Testing.UI.Automation;

// A reporter call failed for a reason that may go away: throttling (429), a server error (5xx), a timeout. The
// dispatcher retries it with exponential back-off, and waits at least RetryAfter when the system named a delay.
public class TransientReporterException : Exception
{
    public TransientReporterException(string message, Exception? innerException = null, TimeSpan? retryAfter = null)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}
