using System;
using System.Collections.Generic;

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

    public string? Title { get; init; }

    public string? Description { get; init; }

    public string? DocumentationLink { get; init; }

    public IReadOnlyList<string>? Labels { get; init; }

    public IReadOnlyDictionary<string, object>? Values { get; init; }
}
