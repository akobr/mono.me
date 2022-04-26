using System;

namespace c0ded0c.Core
{
    public interface IIdentificator : IEquatable<IIdentificator>, IEquatable<string>
    {
        string Hash { get; }

        string Path { get; }

        string FullName { get; }

        string Name { get; }
    }
}
