using System;

namespace _42.Monorepo.Cli.Model.Records
{
    public abstract class ItemRecord : IItemRecord
    {
        protected ItemRecord(string path, IItemRecord? parent)
        {
            Identifier = new Identifier(path, parent);
            Path = path;
            Parent = parent;
        }

        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

        public IIdentifier Identifier { get; }

        public string Path { get; }

        public abstract ItemType Type { get; }

        public IItemRecord? Parent { get; }

        public static bool operator ==(ItemRecord? left, IItemRecord? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ItemRecord? left, IItemRecord? right)
        {
            return !Equals(left, right);
        }

        public bool Equals(IItemRecord? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Type == other.Type
                   && Identifier.Equals(other.Identifier);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            return obj is IItemRecord item && Equals(item);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Path, (int)Type);
        }
    }
}
