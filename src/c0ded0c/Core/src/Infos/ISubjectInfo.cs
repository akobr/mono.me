using System;
using System.Collections.Generic;

namespace c0ded0c.Core
{
    public interface ISubjectInfo : IEquatable<ISubjectInfo>, IEquatable<IIdentificator>, IEquatable<string>
    {
        IIdentificator Key { get; }

        string Name { get; }

        IExpansion? Expansion { get; }

        object? MutableTag { get; }

        IEnumerable<ISubjectInfo> GetChildren();
    }
}
