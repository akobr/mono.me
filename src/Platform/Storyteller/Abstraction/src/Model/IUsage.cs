using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface IUsage : IAnnotation
{
    string SubjectKey { get; }

    string ResponsibilityKey { get; }

    string SubjectName { get; }

    string ResponsibilityName { get; }

    IReadOnlySet<string> Executions { get; }
}
