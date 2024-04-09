using _42.Platform.Storyteller.Access;

namespace _42.Platform.Storyteller.Accessing.Model;

public record class Permission
{
    public required string CreatedById { get; init; }

    public required string AccountId { get; init; }

    public required string AccessPointKey { get; init; }

    public required AccountRole Role { get; init; }
}
