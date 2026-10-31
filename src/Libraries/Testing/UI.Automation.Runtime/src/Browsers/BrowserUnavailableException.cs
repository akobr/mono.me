namespace _42.Testing.UI.Automation.Runtime.Browsers;

// No browser could be launched or connected (not installed, grid unreachable, session limit). The test is Blocked.
public sealed class BrowserUnavailableException : Exception
{
    public BrowserUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
