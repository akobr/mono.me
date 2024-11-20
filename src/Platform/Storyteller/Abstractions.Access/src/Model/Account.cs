using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public record class Account : IAccount
{
    public required string Id { get; init; }

    public required string UserName { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyDictionary<string, AccountRole> AccessMap { get; init; }
}
