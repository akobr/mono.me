using System.Collections.Generic;

namespace _42.Platform.Storyteller.Entities;

public record class ExtendableEntity : Entity
{
    public IReadOnlyList<string>? Labels { get; init; }

    public IReadOnlyDictionary<string, object>? Values { get; init; }
}
