namespace _42.Platform.Cli.AutomationTests.Running;

// A run of sform took longer than its timeout: its process tree was killed. The message holds the redacted output so far.
public sealed class SformTimeoutException : TimeoutException
{
    public SformTimeoutException(string message)
        : base(message)
    {
    }
}
