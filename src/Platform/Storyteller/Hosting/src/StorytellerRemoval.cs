namespace _42.Platform.Storyteller.Sdk.Hosting;

// Which annotation ended a subscription, the execution exe.{subject}.{responsibility}.{context} or the usage
// usg.{subject}.{responsibility}, by its annotation key, and why. The execution is checked first.
public sealed record StorytellerRemoval(string AnnotationKey, StorytellerRemovalReason Reason);
