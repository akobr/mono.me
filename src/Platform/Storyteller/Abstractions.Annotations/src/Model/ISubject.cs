using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface ISubject : IAnnotation
{
    IReadOnlySet<string> Contexts { get; }

    IReadOnlySet<string> Usages { get; }
}
