using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Model
{
    [DebuggerDisplay("{Humanized}")]
    public class Identifier : IIdentifier
    {
        public Identifier(string path, IRecord? parent)
        {
            Name = Path.GetFileName(path);
            Humanized = parent is null
                ? Constants.SOURCE_DIRECTORY_NAME
                : $"{parent.Identifier.Humanized}/{Name}";
            Normalized = Humanized.ToLowerInvariant();

            // TODO: murmur hash
            Hash = Convert.ToBase64String(SHA1.HashData(Encoding.UTF8.GetBytes(Normalized)));
        }

        public string Name { get; }

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
