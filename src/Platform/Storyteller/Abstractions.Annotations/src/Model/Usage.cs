using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record Usage : Annotation, IUsage
{
    public required string SubjectKey { get; init; }

    public required string ResponsibilityKey { get; init; }

    public required string SubjectName { get; init; }

    public required string ResponsibilityName { get; init; }

    public required IReadOnlySet<string> Executions { get; init; }
}
