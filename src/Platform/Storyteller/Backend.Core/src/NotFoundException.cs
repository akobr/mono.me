namespace _42.Platform.Storyteller;

// A resource the operation depends on does not exist. The API answers it with 404.
public sealed class NotFoundException(string message, string errorCode = ErrorCodes.NotFound)
    : StorytellerException(message, errorCode);
