namespace _42.Platform.Storyteller.Access.Models;

public record class AccountCreate
{
    public required string IdentityId { get; init; }

    public required string UserName { get; init; }

    public required string Name { get; init; }

    public required string Organization { get; init; }

    public required string Project { get; init; }
}
