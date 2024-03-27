using System.Collections.Generic;

namespace _42.Platform.Storyteller.Access.Entities;

public record class Account
{
    public string PartitionKey => "access";

    public required string Id { get; init; }

    public required string UserName { get; init; }

    public required string Name { get; init; }

    public required Dictionary<string, AccountRole> AccessMap { get; init; } = new();
}
