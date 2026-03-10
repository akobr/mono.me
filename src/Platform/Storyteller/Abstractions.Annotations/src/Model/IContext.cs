using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface IContext : IAnnotation
{
    string SubjectKey { get; }

    string SubjectName { get; }

    IReadOnlySet<string> ResponsibilityNames { get; }
}
