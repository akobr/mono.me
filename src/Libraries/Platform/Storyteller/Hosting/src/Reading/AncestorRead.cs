using _42.Platform.Storyteller.Sdk.Configuring;

namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// What the read of one ancestor gave: its snapshot, or none. Without a snapshot the ancestor is restricted, when Failure
// is the 403 AnnotationRestricted of its read, and missing when Failure is the 404 of its read, or null when a catalog
// answered null for it. Any other failure is in Failure only for the descendant informer, whose own read of an ancestor
// keeps a failure as an answer: AncestorReader.ReadAsync throws it.
internal sealed record AncestorRead(FullKey Key, StorytellerAnnotation? Annotation, StorytellerConfigurationException? Failure)
{
    // The credential may not read it: it goes to Unreadable.
    public bool IsRestricted => Failure is not null && CatalogReader.IsRestricted(Failure);

    // It does not exist, or is deleted, and its descendants went with it.
    public bool IsMissing => Annotation is null && (Failure is null || CatalogReader.IsMissing(Failure));

    // The read told nothing about it, restricted or failed, so whether it is active is not known.
    public bool IsUndecided => Annotation is null && !IsMissing;
}
