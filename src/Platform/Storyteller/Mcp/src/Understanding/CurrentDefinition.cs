using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// Where the value at a pointer of a system's effective document comes from today. Leaves carry their Value, objects and
// arrays only their ValueKind.
public sealed record CurrentDefinition(string System, string Pointer, bool IsSet)
{
    public JToken? Value { get; init; }

    public string? ValueKind { get; init; }

    public int? Step { get; init; }

    public string? SourceId { get; init; }

    public IReadOnlyList<string>? Via { get; init; }
}
