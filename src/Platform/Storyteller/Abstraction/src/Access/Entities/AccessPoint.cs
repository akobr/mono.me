using System.Collections.Generic;

namespace _42.Platform.Storyteller.Access.Entities;

public record class AccessPoint
{
    public string PartitionKey => "access";

    public string Id => $"apt.{Key}";

    public required string Key { get; init; }

    public Dictionary<string, AccountRole> AccessMap { get; init; } = new();
}
