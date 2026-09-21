using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record class DiffResult
{
    public required DiffStats Stats { get; init; }

    public required IReadOnlyList<DiffHunk> Hunks { get; init; }
}
