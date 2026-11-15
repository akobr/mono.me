using _42.Platform.Storyteller.Sdk.Configuring;

namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// What the read of one ancestor gave: its snapshot, or none. Without a snapshot the ancestor is restricted, when Failure
// is the 403 AnnotationRestricted of its read, and missing otherwise: Failure is then the 404 of its read, or null when
// a catalog answered null for it.
internal sealed record AncestorRead(FullKey Key, StorytellerAnnotation? Annotation, StorytellerConfigurationException? Failure)
{
    // The credential may not read it: it goes to Unreadable.
    public bool IsRestricted => Failure is not null && CatalogReader.IsRestricted(Failure);
}
