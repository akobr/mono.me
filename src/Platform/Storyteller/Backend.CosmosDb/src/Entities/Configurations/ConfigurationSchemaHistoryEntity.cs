using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Entities.Configurations;

public record class ConfigurationSchemaHistoryEntity : Entity
{
    public ulong Version { get; init; }

    public required JObject Content { get; init; }

    // The write that produced this version; null on items stored before the stamps (see GetModified).
    public AuditStamp? Modified { get; init; }

    // Legacy author and time of items stored before the stamps; never written again.
    public string? Author { get; init; }

    public DateTimeOffset? CreationTime { get; init; }

    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public int TimeToLiveInSeconds { get; init; } = 60 * 60 * 24 * 365;
}
