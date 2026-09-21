namespace _42.Platform.Storyteller;

public record class DiffStats
{
    public required int Additions { get; init; }

    public required int Deletions { get; init; }

    public required int Unchanged { get; init; }
}
