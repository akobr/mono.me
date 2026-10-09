namespace _42.Platform.Storyteller;

// One membership of an organization or project, with the account's display data.
public record class AccessPointMember
{
    public required string AccountId { get; init; }

    // Email or preferred user name. Null when the account document is missing.
    public string? UserName { get; init; }

    public string? Name { get; init; }

    public required AccountRole Role { get; init; }
}
