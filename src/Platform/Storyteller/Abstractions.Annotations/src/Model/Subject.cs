using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record Subject : Annotation, ISubject
{

    public IReadOnlySet<string> Contexts { get; init; } = new HashSet<string>(0);

    public IReadOnlySet<string> Usages { get; init; } = new HashSet<string>(0);
}
