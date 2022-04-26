using System;
using System.Diagnostics;

namespace c0ded0c.Core
{
    [DebuggerDisplay("{FullName}")]
    public class Identificator : IIdentificator
    {
        public Identificator(string hash, string fullName, string name, string path)
        {
            Hash = hash ?? throw new ArgumentNullException(nameof(hash));
            FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string Name { get; }

        public string FullName { get; }

        public string Hash { get; }

        public string Path { get; }

        public static implicit operator string(Identificator identificator)
        {
            return identificator?.Hash ?? string.Empty;
        }

        public static bool operator ==(Identificator identificator, string hash)
        {
            return identificator.Equals(hash);
        }

        public static bool operator !=(Identificator identificator, string hash)
        {
            return !identificator.Equals(hash);
        }

        public static bool operator ==(Identificator a, IIdentificator b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(Identificator a, IIdentificator b)
        {
            return !a.Equals(b);
        }

        public bool Equals(IIdentificator? other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            if (ReferenceEquals(other, this))
            {
                return true;
            }

            return Hash.Equals(other.Hash, StringComparison.OrdinalIgnoreCase)
                && FullName.Equals(other.FullName, StringComparison.Ordinal);
        }

        public bool Equals(string? hash)
        {
            return Hash.Equals(hash, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(obj, null))
            {
                return false;
            }

            if (ReferenceEquals(obj, this))
            {
                return true;
            }

            if (obj is IIdentificator identificator)
            {
                return Equals(identificator);
            }

            if (obj is string hash)
            {
                return Equals(hash);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Hash.GetHashCode();
        }
    }
}
