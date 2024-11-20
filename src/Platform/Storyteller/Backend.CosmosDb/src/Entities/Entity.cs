using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities;

public record class Entity
{
    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public virtual required string Id { get; init; }

    [JsonProperty("_ts")]
    [JsonPropertyName("_ts")]
    public uint LastUpdatedEpochTimestamp { get; }

    public required string ProjectName { get; init; }

    public required string ViewName { get; init; }

    public required string AnnotationKey { get; init; }

    public required string Name { get; init; }
}
