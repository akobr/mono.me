using Newtonsoft.Json;

namespace _42.Platform.Storyteller.KeyValues;

// Projection of the listing query: never the ciphertext, and the value of public pairs only.
internal sealed record KeyValueListingRow
{
    [JsonProperty("id")]
    public required string Id { get; init; }

    public string? Key { get; init; }

    public ulong Version { get; init; }

    public required AuditStamp Modified { get; init; }

    public AuditStamp? Deleted { get; init; }

    [JsonProperty("ttl")]
    public int? TimeToLive { get; init; }

    [JsonProperty("_ts")]
    public uint Timestamp { get; init; }

    public KeyValueProtection Protection { get; init; }

    public string? ContentType { get; init; }

    [JsonProperty("PlainValue")]
    public string? Value { get; init; }
}

// Projection of the next-version query of an import: the highest stored version per key.
internal sealed record KeyValueMaxVersionRow
{
    public required string Key { get; init; }

    public ulong Version { get; init; }
}

// Projection of the versions query.
internal sealed record KeyValueVersionRow
{
    public ulong Version { get; init; }

    public required AuditStamp Modified { get; init; }

    public KeyValueProtection Protection { get; init; }

    [JsonProperty("ttl")]
    public int? TimeToLive { get; init; }

    [JsonProperty("_ts")]
    public uint Timestamp { get; init; }
}
