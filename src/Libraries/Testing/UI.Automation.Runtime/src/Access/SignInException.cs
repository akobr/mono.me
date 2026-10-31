namespace _42.Testing.UI.Automation.Runtime.Access;

// The sign-in flow failed for a pool user; the test fails (the sign-in is part of the system under test).
public sealed class SignInException : Exception
{
    public SignInException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
