using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

// A configuration, a template or a schema, or one of their versions, with its document, for an export.
internal sealed record DocumentArchiveRow
{
    [JsonProperty("id")]
    public string? Id { get; init; }

    // Of configurations only.
    public string? AnnotationKey { get; init; }

    public ulong Version { get; init; }

    public JObject? Content { get; init; }

    public required AuditStamp Modified { get; init; }

    // Of configurations only: set while the annotation is deleted.
    public string? AnnotationDeletionId { get; init; }
}

// One page of a stream query.
internal sealed record QueryPage<TRow>
{
    public List<TRow> Documents { get; init; } = [];
}
