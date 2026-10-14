using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Transfers;

// An export or import job: id "{view}.exp.{id}" or "{view}.imp.{id}" in the partition "{project}.transfer" of the
// organization container. It has no AnnotationType, so annotation queries never read it.
public sealed record class ConfigurationTransferEntity
{
    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public required string PartitionKey { get; init; }

    public required string ProjectName { get; init; }

    public required string ViewName { get; init; }

    public required string TransferId { get; init; }

    public required ConfigurationTransferKind Kind { get; init; }

    // A copy of the job's status, for the count of active jobs.
    public required ConfigurationTransferStatus Status { get; init; }

    public required string Author { get; init; }

    public ConfigurationExport? Export { get; init; }

    public ConfigurationImport? Import { get; init; }

    // The retention; every write restarts it.
    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public int TimeToLiveInSeconds { get; init; }
}
