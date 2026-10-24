using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// A value of the effective document and the merge that set it. Leaves carry their Value, objects and arrays only their
// ValueKind. Via is the node path from the step's node to the target, the branch the value arrived through.
public sealed record ExplainedValue
{
    public required string Pointer { get; init; }

    public JToken? Value { get; init; }

    public string? ValueKind { get; init; }

    public required int Step { get; init; }

    public ExplainedSource? Source { get; init; }

    public ValueNode? Node { get; init; }

    public required IReadOnlyList<string> Via { get; init; }

    // The other steps whose document holds a value at the same path; only for scalar members of objects. An object or
    // array merges with the ones before it, so it has none: its members and items carry their own steps.
    public IReadOnlyList<Setter>? AlsoSetBy { get; init; }

    public sealed record ValueNode(string AnnotationKey, int Depth);

    // BeforeWinner: merged before the winning step, so overridden. Otherwise merged after it inside a branch that was
    // merged again later (or with the same value), so not applied.
    public sealed record Setter
    {
        public required int Step { get; init; }

        public required string SourceId { get; init; }

        public JToken? Value { get; init; }

        public string? ValueKind { get; init; }

        public required IReadOnlyList<string> Via { get; init; }

        public required bool BeforeWinner { get; init; }
    }
}
