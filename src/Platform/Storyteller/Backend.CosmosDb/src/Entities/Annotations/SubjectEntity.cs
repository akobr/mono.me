using System.Collections.Generic;

namespace _42.Platform.Storyteller.Entities.Annotations;

public record class SubjectEntity : AnnotationEntity
{
    public required IReadOnlyCollection<string> Contexts { get; init; }

    public required IReadOnlyCollection<string> Usages { get; init; }
}
