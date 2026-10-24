namespace _42.Platform.Storyteller.Mcp.Tools;

// The compact form of an annotation in lists: an API page of whole annotations (descriptions of up to 10,000
// characters, values) does not fit an MCP result. Status is computed as the API computes it; Deletion is set on
// deleted annotations only.
public sealed record AnnotationSummary(
    string AnnotationKey,
    string AnnotationType,
    string Name,
    string? Title,
    AnnotationStatus Status,
    IReadOnlyList<string>? Labels,
    Sdk.AuditStamp? Modified,
    Sdk.AnnotationDeletion? Deletion)
{
    public static AnnotationSummary From(Sdk.Annotation annotation, DateTimeOffset now)
    {
        return new AnnotationSummary(
            annotation.AnnotationKey,
            AnnotationBodies.TypeName(annotation),
            annotation.Name,
            annotation.Title,
            AnnotationStatusExtensions.GetStatus(annotation.IsDisabled, annotation.ValidFrom, annotation.ExpiresAt, now),
            annotation.Labels?.ToList(),
            annotation.Modified,
            annotation.Deletion);
    }

    public static IReadOnlyList<AnnotationSummary> From(IEnumerable<Sdk.Annotation> annotations, DateTimeOffset now)
    {
        return annotations.Select(annotation => From(annotation, now)).ToList();
    }
}
