using System;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.KeyValues.Protection;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.KeyValues;

// A version that is no longer current. Immutable, except for a re-wrap of its data key (Phase 1b).
public record class KeyValueVersionEntity
{
    public required string PartitionKey { get; init; }

    // kvv.{lower-cased key}.{version}
    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonProperty("_ts")]
    [JsonPropertyName("_ts")]
    public uint LastUpdatedEpochTimestamp { get; init; }

    // The rest of the version's lifetime when the item was written.
    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public required int TimeToLiveInSeconds { get; init; }

    public required string ProjectName { get; init; }

    public required string ViewName { get; init; }

    public required string Key { get; init; }

    // The lower-cased key, for queries over many keys at once.
    public required string NormalizedKey { get; init; }

    public required ulong Version { get; init; }

    public required StoredValue Content { get; init; }

    public required string Author { get; init; }

    public required DateTimeOffset CreationTime { get; init; }
}
