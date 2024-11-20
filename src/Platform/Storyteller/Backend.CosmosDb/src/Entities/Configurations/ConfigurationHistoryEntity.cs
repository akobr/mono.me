using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Entities.Configurations;

public record class ConfigurationHistoryEntity : Entity
{
    public ulong Version { get; init; }

    public required JObject Content { get; init; }

    public required string Author { get; init; }

    public DateTimeOffset CreationTime { get; init; }

    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public int TimeToLiveInSeconds { get; init; } = 60 * 60 * 24 * 183; // default to 6 months (183 days)
}
