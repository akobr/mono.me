namespace _42.Platform.Storyteller.Transferring;

// What an export writes: the documents of a view and who exports them when. Only the included kinds are written.
public sealed record class ViewArchiveSource
{
    public required string Organization { get; init; }

    public required string Project { get; init; }

    public required string View { get; init; }

    public required DateTimeOffset ExportedAt { get; init; }

    public required AuditActor ExportedBy { get; init; }

    // The kinds to write; null or empty means all four (ViewArchiveInclude).
    public IReadOnlyList<ViewArchiveContent>? Include { get; init; }

    // The active annotations, with their stamps.
    public IReadOnlyList<Annotation> Annotations { get; init; } = [];

    public IReadOnlyList<SchemaHistory> Schemas { get; init; } = [];

    public IReadOnlyList<TemplateHistory> Templates { get; init; } = [];

    public IReadOnlyList<ConfigurationHistory> Configurations { get; init; } = [];
}
