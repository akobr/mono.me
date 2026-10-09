namespace _42.Platform.Storyteller.Accessing.Model;

public record class AccountCreate
{
    public required string IdentityId { get; init; }

    public required string UserName { get; init; }

    public required string Name { get; init; }

    // Both or neither. Without them the account starts with no memberships, for example from an invitation.
    public string? Organization { get; init; }

    public string? Project { get; init; }
}
