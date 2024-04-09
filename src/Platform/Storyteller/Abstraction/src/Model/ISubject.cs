using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface ISubject : IAnnotation
{
    IReadOnlySet<string> Contexts { get; init; }

    IReadOnlySet<string> Usages { get; init; }
}
