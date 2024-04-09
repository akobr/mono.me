using System.Collections.Generic;

namespace _42.Platform.Storyteller.Access;

public interface IAccessPoint
{
    string Key { get; }

    IReadOnlyDictionary<string, AccountRole> AccessMap { get; }
}
