namespace _42.Platform.Storyteller.Mcp.Understanding;

// A place to write a value: an annotation's own configuration, or the view template of a type. Meaning is the level's
// row of "Where a value belongs" (docs/42for.net/platform/modeling.md); Effects, EffectiveForAll and Evidence are added
// by the advisor.
public sealed record PlacementCandidate
{
    public const string AnnotationKind = "Annotation";
    public const string TemplateKind = "Template";

    public required string Kind { get; init; }

    public string? AnnotationKey { get; init; }

    public string? TemplateTypeCode { get; init; }

    public required string Level { get; init; }

    public required string Meaning { get; init; }

    public required string Reason { get; init; }

    public bool? EffectiveForAll { get; init; }

    public IReadOnlyList<PlacementEffect>? Effects { get; init; }

    public PlacementEvidence? Evidence { get; init; }
}
