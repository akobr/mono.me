using System.Collections.Generic;

namespace _42.Platform.Storyteller.Entities;

public record class Subject : Annotation
{
    public required IReadOnlyCollection<string> Contexts { get; init; }

    public required IReadOnlyCollection<string> Usages { get; init; }
}
