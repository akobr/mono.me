using System;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// A credential could not produce its authorization. Messages name variables, paths and URLs, never a secret.
public sealed class StorytellerAuthenticationException : Exception
{
    public StorytellerAuthenticationException(StorytellerAuthenticationFailureReason reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Reason = reason;
    }

    public StorytellerAuthenticationFailureReason Reason { get; }

    public bool IsTransient => Reason == StorytellerAuthenticationFailureReason.Unavailable;

    internal static StorytellerAuthenticationException Configuration(string message, Exception? innerException = null)
    {
        return new StorytellerAuthenticationException(StorytellerAuthenticationFailureReason.Configuration, message, innerException);
    }
}
