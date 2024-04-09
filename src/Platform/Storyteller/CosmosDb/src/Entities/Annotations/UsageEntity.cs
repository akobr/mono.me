using System.Collections.Generic;

namespace _42.Platform.Storyteller.Entities.Annotations;

public record class UsageEntity : AnnotationEntity
{
    public required string SubjectKey { get; init; }

    public required string ResponsibilityKey { get; init; }

    public required string SubjectName { get; init; }

    public required string ResponsibilityName { get; init; }

    public required IReadOnlyCollection<string> Executions { get; init; }
}
