namespace _42.Testing.UI.Automation.Runtime.Access;

// The sign-in flow failed for a pool user; the test fails (the sign-in is part of the system under test). The
// StorageStateCache gives it the type and the message of the flow's exception with the user's e-mail and password as
// ***, and no inner exception.
public sealed class SignInException : Exception
{
    public SignInException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
