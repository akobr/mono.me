namespace _42.Platform.Storyteller.Mcp.Understanding;

// The answer of suggest_configuration_placement. Systems lists each system's key and whether it has a composition to
// replay (an annotation that does not exist yet has none).
public sealed record PlacementSuggestion
{
    public required IReadOnlyList<SystemKey> Systems { get; init; }

    public required IReadOnlyList<string> Pointers { get; init; }

    public required IReadOnlyList<PlacementCandidate> Candidates { get; init; }

    public string? Notice { get; init; }

    public required IReadOnlyList<CurrentDefinition> Today { get; init; }

    public SchemaPreview? SchemaPreview { get; init; }

    public required IReadOnlyList<string> Notes { get; init; }

    public sealed record SystemKey(string AnnotationKey, bool HasComposition);
}
