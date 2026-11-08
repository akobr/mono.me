namespace _42.Platform.Cli.AutomationTests.Running;

// A check of a run of sform failed; the message holds the command line, the exit code and the redacted output.
public sealed class SformAssertionException : Exception
{
    public SformAssertionException(string message)
        : base(message)
    {
    }

    public SformAssertionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
