namespace _42.Platform.Storyteller.Sdk.Hosting;

// Which annotation ended a subscription, the execution exe.{subject}.{responsibility}.{context} or the usage
// usg.{subject}.{responsibility}, by its annotation key, and why; the execution is checked first. For an annotation of a
// target of IStorytellerCatalog.WatchAsync, the annotation that ended it: the descendant, or for a target that requires
// active ancestors, one of them. The descendant is checked first, then its ancestors, the nearest first.
public sealed record StorytellerRemoval(string AnnotationKey, StorytellerRemovalReason Reason);
