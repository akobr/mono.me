using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record class Responsibility : Annotation, IResponsibility
{
    public IReadOnlySet<string> UnitNames { get; init; } = new HashSet<string>(0);
}
