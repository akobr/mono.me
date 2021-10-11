using System;

namespace _42.Monorepo.Cli.Model.Records
{
    public abstract class Record : IRecord
    {
        protected Record(string path, IRecord? parent)
        {
            Identifier = new Identifier(path, parent);
            Path = path;
            Parent = parent;
        }

        public IIdentifier Identifier { get; }

        public string Name => Identifier.Name;

        public string Path { get; }

        public virtual string RepoRelativePath => Identifier.Humanized;

        public abstract ItemType Type { get; }

        public IRecord? Parent { get; }

        public static bool operator ==(Record? left, IRecord? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Record? left, IRecord? right)
        {
            return !Equals(left, right);
        }

        public bool Equals(IRecord? other)
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

            return obj is IRecord item && Equals(item);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Path, (int)Type);
        }
    }
}
