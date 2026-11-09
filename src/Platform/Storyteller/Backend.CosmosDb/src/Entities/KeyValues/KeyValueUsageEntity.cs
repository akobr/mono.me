using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.KeyValues;

// The meters of one project for one UTC day, in the organization container, partition "{project}.usage".
public record class KeyValueUsageEntity
{
    public const string IdPrefix = "kvu.";

    public required string PartitionKey { get; init; }

    // The organization of the item: the first level of the hierarchical partition key of the container
    // organizations (/Organization, /PartitionKey). Cosmos DB refuses a write whose key does not match it.
    public required string Organization { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public required int TimeToLiveInSeconds { get; init; }

    public required string ProjectName { get; init; }

    // yyyy-MM-dd
    public required string Day { get; init; }

    // Operations, added as they happen.
    public Dictionary<string, long> Counters { get; init; } = new();

    // Stored keys and versions, from the daily snapshot.
    public Dictionary<string, long> Gauges { get; init; } = new();
}
