namespace _42.Testing.UI.Automation.Runtime;

// The target cannot run the tests. A check (IUiAutomationSetup.CheckTargetAsync) throws it with a message that says
// what is wrong and what to fix; the session throws it from its start, "The target check failed: <message>", for any
// exception of the check.
public sealed class TargetCheckException : Exception
{
    public TargetCheckException(string message)
        : base(message)
    {
    }

    public TargetCheckException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
