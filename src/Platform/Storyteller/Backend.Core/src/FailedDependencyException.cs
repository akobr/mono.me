namespace _42.Platform.Storyteller;

// A resource the customer controls (their key) refused or vanished. The API answers it with 424.
public sealed class FailedDependencyException(string message, string errorCode, Exception? innerException = null)
    : StorytellerException(message, errorCode, innerException);
