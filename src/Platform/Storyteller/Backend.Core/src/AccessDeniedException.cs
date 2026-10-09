namespace _42.Platform.Storyteller;

// The caller is authenticated, but its role does not allow the operation. The API answers it with 403.
public sealed class AccessDeniedException(string message, string errorCode = ErrorCodes.AccessDenied)
    : StorytellerException(message, errorCode);
