namespace _42.Platform.Storyteller.Transferring;

// A stored job: the public model of its kind and what the worker needs besides it.
public sealed record class ViewTransferRecord
{
    public required string Organization { get; init; }

    public required string Project { get; init; }

    public required string View { get; init; }

    public required ViewTransferKind Kind { get; init; }

    public required string Id { get; init; }

    public ViewExport? Export { get; init; }

    public ViewImport? Import { get; init; }

    // The concurrency token of the stored item; null before the first write.
    public string? ETag { get; init; }

    public ViewTransferStatus Status => Export?.Status ?? Import?.Status ?? ViewTransferStatus.Failed;

    public ViewTransferMessage ToMessage() => new(Organization, Project, View, Kind, Id);
}
