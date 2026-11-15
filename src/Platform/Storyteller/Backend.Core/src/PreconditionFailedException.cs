namespace _42.Platform.Storyteller;

// A condition the caller stated (an expected version) does not hold. The API answers it with 412.
// Not sealed: ProjectSettingsPreconditionFailedException carries the current version too.
public class PreconditionFailedException(string message, string errorCode)
    : StorytellerException(message, errorCode);
