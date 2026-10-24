namespace _42.Platform.Storyteller.Mcp.Understanding;

// The explanation of an effective configuration. Hash is the composition's, EffectiveHash the effective read's; IsStale
// is true when they differ.
public sealed record ConfigurationExplanation
{
    public required string AnnotationKey { get; init; }

    public string? Pointer { get; init; }

    public string? Hash { get; init; }

    public string? EffectiveHash { get; init; }

    public bool IsStale { get; init; }

    public required IReadOnlyList<ExplainedValue> Values { get; init; }

    public bool IsTruncated { get; init; }

    public required IReadOnlyList<ExplainedSource> Sources { get; init; }

    public required IReadOnlyList<ExplainedBinding> Bindings { get; init; }

    public bool KeyValueLookupsTruncated { get; init; }

    public required ConfigurationConsumers Consumers { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }
}
