namespace _42.Platform.Storyteller.Annotating;

// Items: per input annotation, in input order. AncestorsCreated: the keys outside the input created with defaults,
// in the order they were created. RelationUpdates: the active annotations whose relation arrays were replaced (in a
// dry run: would be).
public sealed record class AnnotationBulkResult(
    IReadOnlyList<AnnotationBulkItem> Items,
    IReadOnlyList<string> AncestorsCreated,
    int RelationUpdates);
