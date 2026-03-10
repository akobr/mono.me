using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface IResponsibility : IAnnotation
{
    IReadOnlySet<string> UnitNames { get; }
}
