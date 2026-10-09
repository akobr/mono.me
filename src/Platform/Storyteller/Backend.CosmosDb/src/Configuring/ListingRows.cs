using System;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Configuring;

// Projections of the listing queries: identity and audit fields only, never the JSON documents.
internal sealed record ConfigurationListingRow
{
    public string? AnnotationKey { get; init; }

    public ulong Version { get; init; }

    public string? Author { get; init; }

    public string? CalculatedContentHash { get; init; }

    public bool HasContent { get; init; }

    [JsonProperty("_ts")]
    public long Timestamp { get; init; }
}

internal sealed record DocumentListingRow
{
    [JsonProperty("id")]
    public required string Id { get; init; }

    public ulong Version { get; init; }

    public string? Author { get; init; }

    [JsonProperty("_ts")]
    public long Timestamp { get; init; }
}

internal static class ListingTimestamps
{
    // Cosmos _ts is the last write in Unix seconds; 0 means the projection had none.
    public static DateTimeOffset? ToUpdatedAt(long timestamp)
    {
        return timestamp > 0 ? DateTimeOffset.FromUnixTimeSeconds(timestamp) : null;
    }
}
