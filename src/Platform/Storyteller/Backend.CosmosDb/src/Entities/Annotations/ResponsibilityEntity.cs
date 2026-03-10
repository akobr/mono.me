using System.Collections.Generic;

namespace _42.Platform.Storyteller.Entities.Annotations;

public record class ResponsibilityEntity : AnnotationEntity
{
    public required IReadOnlyCollection<string> UnitNames { get; init; }
}
