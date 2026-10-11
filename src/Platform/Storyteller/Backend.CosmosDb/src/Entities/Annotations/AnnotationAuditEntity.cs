using System.Collections.Generic;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Annotations;

// The audit of one annotation: id "{view}.adt.{annotationKey}", in the annotation's partition, so it is written
// in the same transactional batch as the annotation.
public record class AnnotationAuditEntity : Entity
{
    // Not named AnnotationType: annotation queries filter on AnnotationType, so an audit item never reads as an annotation.
    public required AnnotationType AuditedType { get; init; }

    // Oldest first, appended.
    public required IReadOnlyList<AnnotationAuditEntry> Entries { get; init; }

    // Older entries were dropped to keep the document within its size limit.
    public bool IsTrimmed { get; init; }

    // UTF-8 size of the serialized item as last written, for monitoring queries.
    public int SizeInBytes { get; init; }

    // Only while the annotation is soft-deleted; the audit of an active annotation never expires.
    [JsonProperty("ttl")]
    [JsonPropertyName("ttl")]
    public int? TimeToLiveInSeconds { get; init; }
}
