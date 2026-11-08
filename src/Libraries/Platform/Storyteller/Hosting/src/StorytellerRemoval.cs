namespace _42.Platform.Storyteller.Sdk.Hosting;

// Which annotation ended a subscription, the execution exe.{subject}.{responsibility}.{context} or the usage
// usg.{subject}.{responsibility}, by its annotation key, and why; the execution is checked first. For an annotation of a
// target of IStorytellerCatalog.WatchAsync, its own annotation key.
public sealed record StorytellerRemoval(string AnnotationKey, StorytellerRemovalReason Reason);
