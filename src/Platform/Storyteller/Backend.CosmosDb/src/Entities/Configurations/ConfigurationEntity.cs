using System.Text.Json.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Entities.Configurations;

public record class ConfigurationEntity : ExtendableEntity
{
    public bool IsServerSubstitutionDisabled { get; init; }

    public bool IsCachingDisabled { get; init; }

    public ulong Version { get; init; }

    public ulong AffectedCounter { get; init; }

    public required JObject Content { get; init; }

    // The write that produced the current version; null on items stored before the stamps (see GetModified).
    public AuditStamp? Modified { get; init; }

    // Legacy author string of items stored before the stamps; never written again.
    public string? Author { get; init; }

    public JObject? CalculatedContent { get; init; }

    public string? CalculatedContentHash { get; init; }

    // Set while the annotation is soft-deleted: the configuration is hidden and expires with the annotation.
    public string? AnnotationDeletionId { get; init; }

    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public int? TimeToLiveInSeconds { get; init; }
}
