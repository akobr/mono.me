using System.Diagnostics;
using System.IO;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Model
{
    [DebuggerDisplay("{Humanized}")]
    public class Identifier : IIdentifier
    {
        public Identifier(string path, IItemRecord? parent)
        {
            if (parent is null)
            {
                Normalized = Humanized = Constants.SOURCE_DIRECTORY_NAME;
            }
            else
            {
                var name = Path.GetFileNameWithoutExtension(path);
                Humanized = $"{parent.Identifier.Humanized}/{name}";
                Normalized = Humanized.ToLowerInvariant();
            }

            Hash = Normalized; // TODO: create Murmur hash here
        }

        public string Normalized { get; }

        public string Humanized { get; }

        public string Hash { get; }

        public static bool operator ==(Identifier? left, IIdentifier? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(Identifier? left, IIdentifier? right)
        {
            return !Equals(left, right);
        }

        public bool Equals(IIdentifier? other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return Hash.EqualsOrdinal(other.Hash);
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

            return obj is IIdentifier identifier && Equals(identifier);
        }

        public override int GetHashCode()
        {
            return Hash.GetHashCode();
        }
    }
}
