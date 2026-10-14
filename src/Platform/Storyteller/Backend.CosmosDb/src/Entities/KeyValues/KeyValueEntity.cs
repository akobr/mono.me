using System.Text.Json.Serialization;
using _42.Platform.Storyteller.KeyValues.Protection;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.KeyValues;

// The catalog item of one key: its current version. Standalone like ViewEntity, because a pair has no
// annotation key. It carries ViewName, so view discovery finds a view that holds only pairs.
public record class KeyValueEntity
{
    public required string PartitionKey { get; init; }

    // kv.{lower-cased key}
    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonProperty("_ts")]
    [JsonPropertyName("_ts")]
    public uint LastUpdatedEpochTimestamp { get; init; }

    // Present on items loaded by a query, for the conditional writes of an import.
    [JsonProperty("_etag")]
    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }

    // Set only while the key is soft-deleted.
    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public int? TimeToLiveInSeconds { get; init; }

    public required string ProjectName { get; init; }

    public required string ViewName { get; init; }

    // The casing of the first write.
    public required string Key { get; init; }

    public required ulong Version { get; init; }

    public required StoredValue Content { get; init; }

    public required AuditStamp Created { get; init; }

    // The write that produced the current version.
    public required AuditStamp Modified { get; init; }

    // Set only while the key is soft-deleted.
    public AuditStamp? Deleted { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public bool IsDeleted => Deleted is not null;
}
