using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record class DiffLine
{
    public required DiffChangeType Type { get; init; }

    public required string Content { get; init; }

    public int? OldLineNumber { get; init; }

    public int? NewLineNumber { get; init; }

    public IReadOnlyList<DiffSegment>? Segments { get; init; }
}
