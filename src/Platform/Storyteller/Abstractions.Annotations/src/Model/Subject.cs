using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record Subject : Annotation, ISubject
{

    public IReadOnlySet<string> ContextNames { get; init; } = new HashSet<string>(0);

    public IReadOnlySet<string> ResponsibilityNames { get; init; } = new HashSet<string>(0);
}
