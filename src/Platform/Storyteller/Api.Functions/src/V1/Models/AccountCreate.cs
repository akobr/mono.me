namespace _42.Platform.Storyteller.Api.V1.Models;

public record class AccountCreate
{
    public required string Organization { get; init; }

    public required string Project { get; init; }
}
