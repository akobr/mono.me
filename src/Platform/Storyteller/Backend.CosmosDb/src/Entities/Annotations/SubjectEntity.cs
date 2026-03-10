using System.Collections.Generic;

namespace _42.Platform.Storyteller.Entities.Annotations;

public record class SubjectEntity : AnnotationEntity
{
    public required IReadOnlyCollection<string> ContextNames { get; init; }

    public required IReadOnlyCollection<string> ResponsibilityNames { get; init; }
}
