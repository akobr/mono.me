using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public interface IAccount
{
    string Id { get; }

    string UserName { get; }

    string Name { get; }

    IReadOnlyDictionary<string, AccountRole> AccessMap { get; }
}
