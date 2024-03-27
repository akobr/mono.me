namespace _42.Platform.Storyteller.Access.Models;

public record class AccessPointCreate
{
    public required string Organization { get; init; }

    public string? Project { get; init; }

    public required string OwnerId { get; init; }
}
