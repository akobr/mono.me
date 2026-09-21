using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record class DiffHunk
{
    public required int OldStart { get; init; }

    public required int OldCount { get; init; }

    public required int NewStart { get; init; }

    public required int NewCount { get; init; }

    public required IReadOnlyList<DiffLine> Lines { get; init; }
}
