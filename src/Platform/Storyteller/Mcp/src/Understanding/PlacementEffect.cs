namespace _42.Platform.Storyteller.Mcp.Understanding;

// Whether a value written on a candidate wins at the pointer of one system's effective document. When it does not, the
// winning step and the branch it came through (Via). Effective is null when the system could not be replayed, or when a
// pointer does not stand for one scalar value there (through an array, or at an object or array); Note says why.
public sealed record PlacementEffect(string System, string Pointer, bool? Effective)
{
    public int? WinningStep { get; init; }

    public string? WinningSourceId { get; init; }

    public IReadOnlyList<string>? Via { get; init; }

    public string? Note { get; init; }
}
