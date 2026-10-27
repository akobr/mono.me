namespace _42.Platform.Storyteller.Sdk.Hosting;

// One event of the informer about a subscription: an active execution exe.{subject}.{responsibility}.{context} with its
// active usage usg.{subject}.{responsibility}, whose labels and values are the subject's entitlement facts. Added and
// Changed carry the snapshots of point reads; Removed carries the snapshots held before, and Removal says which
// annotation ended the subscription and why.
public sealed record StorytellerExecutionChange(
    StorytellerChangeKind Kind,
    StorytellerExecution Execution,
    StorytellerAnnotation Usage,
    StorytellerRemoval? Removal = null);
