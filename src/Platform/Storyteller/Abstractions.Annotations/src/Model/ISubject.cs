using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface ISubject : IAnnotation
{
    IReadOnlySet<string> ContextNames { get; }

    IReadOnlySet<string> ResponsibilityNames { get; }
}
