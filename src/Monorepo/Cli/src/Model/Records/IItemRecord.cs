using System;

namespace _42.Monorepo.Cli.Model.Records
{
    public interface IItemRecord : IEquatable<IItemRecord>
    {
        public IIdentifier Identifier { get; }

        string Name { get; }

        string Path { get; }

        ItemType Type { get; }

        IItemRecord? Parent { get; }
    }
}
