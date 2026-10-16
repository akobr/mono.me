namespace _42.Platform.Storyteller.Transferring;

// What an export writes: the own documents of a view and who exports them when.
public sealed record class ViewArchiveSource
{
    public required string Organization { get; init; }

    public required string Project { get; init; }

    public required string View { get; init; }

    public required DateTimeOffset ExportedAt { get; init; }

    public required AuditActor ExportedBy { get; init; }

    public required IReadOnlyList<ConfigurationHistory> Histories { get; init; }
}
