using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.KeyValues;

// One audit event, in the organization container, partition "{project}.audit". Expires by ttl.
public record class KeyValueAuditEntity
{
    public const string IdPrefix = "kva.";

    public required string PartitionKey { get; init; }

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public required int TimeToLiveInSeconds { get; init; }

    public required string ProjectName { get; init; }

    // ISO 8601 in UTC with seven fractional digits, so the string order is the time order.
    public required string Time { get; init; }

    public string? View { get; init; }

    public string? Key { get; init; }

    public ulong? Version { get; init; }

    public KeyValueProtection? Protection { get; init; }

    public required KeyValueAuditOperation Operation { get; init; }

    public required AuditActor Actor { get; init; }

    public required KeyValueChannel Channel { get; init; }

    public int Count { get; init; } = 1;

    public string? CorrelationId { get; init; }

    public string? Detail { get; init; }

    public static string FormatTime(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }
}
