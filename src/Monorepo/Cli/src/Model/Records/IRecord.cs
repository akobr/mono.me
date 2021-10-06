using System;

namespace _42.Monorepo.Cli.Model.Records
{
    public interface IRecord : IEquatable<IRecord>
    {
        public IIdentifier Identifier { get; }

        string Name { get; }

        string Path { get; }

        ItemType Type { get; }

        IRecord? Parent { get; }
    }
}
