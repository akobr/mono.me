using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface IExecution : IAnnotation
{
    string ResponsibilityKey { get; }

    string SubjectKey { get; }

    string ContextKey { get; }

    string UsageKey { get; init; }

    string SubjectName { get; }

    string ResponsibilityName { get; }

    string ContextName { get; }

    IReadOnlySet<string> UnitNames { get; }
}
