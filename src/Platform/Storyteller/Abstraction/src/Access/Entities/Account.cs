using System.Collections.Generic;

namespace _42.Platform.Storyteller.Access.Entities;

public record class Account
{
    public string PartitionKey => "access";

    public string Id => $"act.{Key}";

    public required string SystemId { get; init; }

    public required string Key { get; init; }

    public required string Name { get; init; }

    public required Dictionary<string, AccountRole> AccessMap { get; init; } = new();
}
