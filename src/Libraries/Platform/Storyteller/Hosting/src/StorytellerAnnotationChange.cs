namespace _42.Platform.Storyteller.Sdk.Hosting;

// One event of IStorytellerCatalog.WatchAsync about an annotation of its target. Added and Changed carry the snapshot of
// a point read; Removed carries the snapshot held before, and Removal says why it left: its own annotation key and the
// reason.
public sealed record StorytellerAnnotationChange<TAnnotation>(
    StorytellerChangeKind Kind,
    TAnnotation Annotation,
    StorytellerRemoval? Removal = null)
    where TAnnotation : StorytellerAnnotation;
