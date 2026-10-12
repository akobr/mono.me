namespace _42.Platform.Storyteller;

// The deployment has no backend for the requested feature. The API answers it with 501.
public sealed class FeatureNotConfiguredException(string message, string errorCode)
    : StorytellerException(message, errorCode);
