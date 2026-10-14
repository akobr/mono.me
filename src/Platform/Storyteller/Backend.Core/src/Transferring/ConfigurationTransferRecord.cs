namespace _42.Platform.Storyteller.Transferring;

// A stored job: the public model of its kind and what the worker needs besides it.
public sealed record class ConfigurationTransferRecord
{
    public required string Organization { get; init; }

    public required string Project { get; init; }

    public required string View { get; init; }

    public required ConfigurationTransferKind Kind { get; init; }

    public required string Id { get; init; }

    // The requester as the Author of configuration versions written by an import.
    public required string Author { get; init; }

    public ConfigurationExport? Export { get; init; }

    public ConfigurationImport? Import { get; init; }

    // The concurrency token of the stored item; null before the first write.
    public string? ETag { get; init; }

    public ConfigurationTransferStatus Status => Export?.Status ?? Import?.Status ?? ConfigurationTransferStatus.Failed;

    public ConfigurationTransferMessage ToMessage() => new(Organization, Project, View, Kind, Id);
}
