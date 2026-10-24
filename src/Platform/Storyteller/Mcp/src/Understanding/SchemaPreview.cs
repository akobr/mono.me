namespace _42.Platform.Storyteller.Mcp.Understanding;

// The schema check of a write, previewed on the candidate's stored document with the fragment merged in. Errors are
// "{path}: {kind}", as the API reports them. Reason says why nothing was previewed.
public sealed record SchemaPreview
{
    public required string AnnotationKey { get; init; }

    public required bool IsPreviewed { get; init; }

    public bool? IsValid { get; init; }

    public IReadOnlyList<string>? Errors { get; init; }

    public string? Reason { get; init; }

    public string Note { get; init; } = "A preview: the API decides at write time, against the schemas of that moment.";

    public static SchemaPreview NotPreviewed(string annotationKey, string reason)
    {
        return new SchemaPreview { AnnotationKey = annotationKey, IsPreviewed = false, Reason = reason };
    }
}
