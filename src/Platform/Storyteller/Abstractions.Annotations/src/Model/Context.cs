using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record class Context : Annotation, IContext
{
    public required string SubjectKey { get; init; }

    public required string SubjectName { get; init; }

    public IReadOnlySet<string> Executions { get; init; } = new HashSet<string>(0);
}
