namespace _42.Testing.UI.Automation.Runtime.TUnit;

// A UI test cannot begin because the run's settings did not load or its session did not start (e.g. the target check
// failed): the same cause for every UI test of the run, so [ConfiguredRetry] never runs one again for it.
public sealed class UiAutomationUnavailableException : InvalidOperationException
{
    public UiAutomationUnavailableException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
