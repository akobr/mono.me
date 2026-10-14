using System;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Configuring;

// Projections of the listing queries: identity and audit fields only, never the JSON documents.
internal sealed record ConfigurationListingRow
{
    public string? AnnotationKey { get; init; }

    public ulong Version { get; init; }

    public AuditStamp? Modified { get; init; }

    // Legacy author of items stored before the stamps.
    public string? Author { get; init; }

    public string? CalculatedContentHash { get; init; }

    public bool HasContent { get; init; }

    [JsonProperty("_ts")]
    public long Timestamp { get; init; }

    public AuditStamp? GetModified() => ListingStamps.ToModified(Modified, Author, Timestamp);
}

internal sealed record DocumentListingRow
{
    [JsonProperty("id")]
    public required string Id { get; init; }

    public ulong Version { get; init; }

    public AuditStamp? Modified { get; init; }

    // Legacy author of items stored before the stamps.
    public string? Author { get; init; }

    [JsonProperty("_ts")]
    public long Timestamp { get; init; }

    public AuditStamp? GetModified() => ListingStamps.ToModified(Modified, Author, Timestamp);
}

internal static class ListingStamps
{
    // An item stored before the stamps gets its author string and its last write (Cosmos _ts, in Unix seconds;
    // 0 means the projection had none).
    public static AuditStamp? ToModified(AuditStamp? modified, string? author, long timestamp)
    {
        if (modified is not null)
        {
            return modified;
        }

        return timestamp > 0 ? LegacyAuthors.ToStamp(author, DateTimeOffset.FromUnixTimeSeconds(timestamp)) : null;
    }
}
