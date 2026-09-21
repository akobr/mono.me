namespace _42.Platform.Storyteller;

public record class DiffSegment
{
    public required string Text { get; init; }

    public required bool IsChange { get; init; }
}
