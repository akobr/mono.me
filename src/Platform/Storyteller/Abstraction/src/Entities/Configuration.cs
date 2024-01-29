using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Entities;

public record class Configuration : ExtendableStoredItem
{
    public required JsonObject Content { get; init; }

    public IReadOnlyCollection<string>? Ancestors { get; init; }
}
