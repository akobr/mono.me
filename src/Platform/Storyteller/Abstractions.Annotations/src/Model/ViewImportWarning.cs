namespace _42.Platform.Storyteller;

// Something a written document of an import should be checked for: a forced schema violation (SchemaViolation),
// ancestors created with defaults (AncestorCreated), or re-created deleted annotations (AnnotationRecreated).
public record class ViewImportWarning
{
    public required string Code { get; init; }

    public required string Message { get; init; }
}
