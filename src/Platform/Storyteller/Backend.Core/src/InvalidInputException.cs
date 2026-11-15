namespace _42.Platform.Storyteller;

// The request breaks a domain rule about its own content, for example an invalid name. The API answers it with 400.
// Not sealed: ProjectSettingsInvalidException carries the problems of its rules too.
public class InvalidInputException(string message, string errorCode)
    : StorytellerException(message, errorCode);
