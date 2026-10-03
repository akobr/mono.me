using System;

namespace _42.Platform.Cli.Authentication;

// Provider-neutral sign-in failure. Messages never contain tokens.
public sealed class AuthenticationException : Exception
{
    public AuthenticationException(AuthenticationFailureReason reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public AuthenticationFailureReason Reason { get; }
}
