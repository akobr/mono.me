using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.Annotating;

// The outcome of one input annotation, with the codes of an import (Created with AncestorCreated, Unchanged, Skipped
// with DocumentExists, AnnotationDeleted or DependencyFailed, Failed with InvalidAnnotation or DuplicateFile).
public sealed record class AnnotationBulkItem(AnnotationKey Key, ViewImportResult Result);
