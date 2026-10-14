using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Entities.Configurations;

public record class GenerateTemplateHistoryEntity : Entity
{
    public ulong Version { get; init; }

    public required JObject Content { get; init; }

    // The write that produced this version.
    public required AuditStamp Modified { get; init; }

    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public int TimeToLiveInSeconds { get; init; } = 60 * 60 * 24 * 365; // default to 12 months (365 days)
}
