namespace _42.Platform.Storyteller.Mcp.Understanding;

// The state of a candidate today: whether it exists, what to do first when it does not (Next), and how far a value
// written on it reaches (Descendants of the first API page; AtLeast when there are more pages).
public sealed record PlacementEvidence
{
    public const string Exists = "Exists";
    public const string Deleted = "Deleted";
    public const string Missing = "Missing";

    public required string State { get; init; }

    public string? Next { get; init; }

    public int? Descendants { get; init; }

    public bool? AtLeast { get; init; }

    public required string Reach { get; init; }
}
