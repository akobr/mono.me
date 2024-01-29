namespace _42.Platform.Storyteller.Access.Models;

public record class Permission
{
    public required string CreatedByKey { get; init; }

    public required string AccountKey { get; init; }

    public required string AccessPointKey { get; init; }

    public required AccountRole Role { get; init; }
}
