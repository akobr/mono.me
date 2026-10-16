namespace _42.Platform.Storyteller.Transferring;

// What every document of one import shares (ImportAnnotationAsync, ImportTemplateAsync, …).
public sealed record class ViewImportOptions
{
    // The caller of the import: written versions are stamped with it, and annotations record it in their audit.
    public required AuditActor Actor { get; init; }

    // What happens to a document the view already has; an equal one is Unchanged either way.
    public ViewImportExisting Existing { get; init; }

    // Store schemas and configurations that break each other, with the warning SchemaViolation.
    public bool Force { get; init; }

    // Decide every outcome and warning without writing.
    public bool DryRun { get; init; }
}
