namespace _42.Platform.Storyteller.Annotating;

// A write's stored annotation with the storage marker of its annotation item after the write: the version a later
// conditional write names (an HTTP ETag). Marker is null when the service cannot tell.
public sealed record AnnotationWriteResult(Annotation Annotation, string? Marker);
