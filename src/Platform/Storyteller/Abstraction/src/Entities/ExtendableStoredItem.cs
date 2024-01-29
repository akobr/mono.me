using System.Collections.Generic;

namespace _42.Platform.Storyteller.Entities;

public record class ExtendableStoredItem : StoredItem
{
    public IReadOnlyCollection<string>? Labels { get; init; }

    public IReadOnlyDictionary<string, object>? Values { get; init; }
}
