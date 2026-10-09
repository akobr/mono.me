namespace _42.Platform.Storyteller.Api.V1.Models;

public record class AccountCreate
{
    // Both or neither. Without them the account starts with no memberships; join through an invitation.
    public string? Organization { get; init; }

    public string? Project { get; init; }
}
