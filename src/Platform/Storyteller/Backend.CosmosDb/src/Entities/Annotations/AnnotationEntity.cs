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
}
