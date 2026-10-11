using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Annotations;

public record class AnnotationEntity : ExtendableEntity
{
    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public override required string Id
    {
        get => $"{ViewName}.{AnnotationKey}";
        init { }
    }

    public required AnnotationType AnnotationType { get; init; }

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? DocumentationLink { get; init; }

    public bool? IsDisabled { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? TimeZone { get; init; }

    public AuditStamp? Created { get; init; }

    public AuditStamp? Modified { get; init; }

    // Set while the annotation is soft-deleted; PurgeAt is never stored.
    public AnnotationDeletion? Deletion { get; init; }

    // Only while the annotation is soft-deleted: Cosmos removes the item when it runs out.
    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public int? TimeToLiveInSeconds { get; init; }
}
