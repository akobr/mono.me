using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace _42.Platform.Storyteller;

public record class Annotation : IAnnotation
{
    public required string ProjectName { get; init; }

    public required string ViewName { get; init; }

    public required string AnnotationKey { get; init; }

    public required string Name { get; init; }

    public required AnnotationType AnnotationType { get; init; }

    public bool? IsDisabled { get; init; }

    public DateTimeOffset? ValidFrom { get; init; }

    public DateTimeOffset? ExpiresAt { get; init; }

    public string? TimeZone { get; init; }

    [MaxLength(AnnotationLimits.TitleMaxLength)]
    public string? Title { get; init; }

    [MaxLength(AnnotationLimits.DescriptionMaxLength)]
    public string? Description { get; init; }

    [MaxLength(AnnotationLimits.DocumentationLinkMaxLength)]
    public string? DocumentationLink { get; init; }

    public IReadOnlyList<string>? Labels { get; init; }

    public IReadOnlyDictionary<string, object>? Values { get; init; }

    // Server-owned: every stored annotation has both stamps, and the body of a write ignores them,
    // so they are null only in request bodies.
    public AuditStamp? Created { get; init; }

    public AuditStamp? Modified { get; init; }

    // Set only by the reads of deleted annotations.
    public AnnotationDeletion? Deletion { get; init; }
}
