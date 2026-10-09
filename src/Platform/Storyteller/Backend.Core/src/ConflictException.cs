namespace _42.Platform.Storyteller;

// The operation contradicts the current state, for example a duplicate or the last owner. The API answers it with 409.
public sealed class ConflictException(string message, string errorCode = ErrorCodes.Conflict)
    : StorytellerException(message, errorCode);
