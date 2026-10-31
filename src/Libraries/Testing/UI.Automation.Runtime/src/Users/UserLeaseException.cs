namespace _42.Testing.UI.Automation.Runtime.Users;

// No pool user could be leased: none has the role, or none became free in time. The test is Blocked, not Failed.
public sealed class UserLeaseException : Exception
{
    public UserLeaseException(string message)
        : base(message)
    {
    }
}
