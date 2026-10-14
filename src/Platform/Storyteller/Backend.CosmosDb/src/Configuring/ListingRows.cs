using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Configuring;

// Projections of the listing queries: identity and audit fields only, never the JSON documents.
internal sealed record ConfigurationListingRow
{
    public string? AnnotationKey { get; init; }

    public ulong Version { get; init; }

    public required AuditStamp Modified { get; init; }

    public string? CalculatedContentHash { get; init; }

    public bool HasContent { get; init; }
}

internal sealed record DocumentListingRow
{
    [JsonProperty("id")]
    public required string Id { get; init; }

    public ulong Version { get; init; }

    public required AuditStamp Modified { get; init; }
}
