using System;
using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface IAnnotation
{
    string ProjectName { get; }

    string ViewName { get; }

    string AnnotationKey { get; }

    string Name { get; }

    AnnotationType AnnotationType { get; }

    bool? IsDisabled { get; }

    DateTimeOffset? ValidFrom { get; }

    DateTimeOffset? ExpiresAt { get; }

    string? TimeZone { get; }

    string? Title { get; }

    string? Description { get; }

    string? DocumentationLink { get; }

    IReadOnlyList<string>? Labels { get; }

    IReadOnlyDictionary<string, object>? Values { get; }
}
