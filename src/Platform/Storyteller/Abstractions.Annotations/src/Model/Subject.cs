using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record Subject : Annotation, ISubject
{
    public required IReadOnlySet<string> Contexts { get; init; }
    public required IReadOnlySet<string> Usages { get; init; }
}
