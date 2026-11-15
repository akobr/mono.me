using System;
using System.Globalization;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

// One access audit record. It lives in the partition of the change it records, which writes it in the same transactional
// batch: core "access" (memberships, access points, policies) or organizations (organization, "{project}.access") for
// machines and shared certificates, (organization, "{project}.federation") for trusted issuers. Nothing updates or
// deletes it; it expires by ttl.
public record class AccessAuditEntity
{
    public const string IdPrefix = "aca.";

    public required string PartitionKey { get; init; }

    // The organization of the record: the first level of the key in the container organizations, a filtered property
    // in core.
    public required string Organization { get; init; }

    // The id order is the time order: aca.{yyyyMMddHHmmssfffffff}.{guid}.
    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public required int TimeToLiveInSeconds { get; init; }

    // Null for a record of the organization itself.
    public string? Project { get; init; }

    public required string AccessPointKey { get; init; }

    // ISO 8601 in UTC with seven fractional digits, so the string order is the time order.
    public required string Time { get; init; }

    public required AccessAuditAction Action { get; init; }

    public required AuditActor Actor { get; init; }

    public AccessAuditTarget? Target { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }

    public string? Detail { get; init; }

    public string? CorrelationId { get; init; }

    public static string NewId(DateTimeOffset time)
    {
        return $"{IdPrefix}{time.UtcDateTime.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture)}.{Guid.NewGuid():N}";
    }

    public AccessAuditEntry ToEntry() => new()
    {
        Id = Id,
        Time = DateTimeOffset.Parse(Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
        Action = Action,
        Actor = Actor,
        AccessPointKey = AccessPointKey,
        Target = Target,
        OldValue = OldValue,
        NewValue = NewValue,
        Detail = Detail,
        CorrelationId = CorrelationId,
    };
}
