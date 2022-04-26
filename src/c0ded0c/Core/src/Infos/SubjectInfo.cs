using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace c0ded0c.Core
{
    [DebuggerDisplay("{Key}")]
    public abstract partial class SubjectInfo : ISubjectInfo
    {
        protected SubjectInfo(IIdentificator key)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
        }

        protected SubjectInfo(ISubjectInfo toClone)
        {
            Key = toClone.Key;
            Expansion = toClone.Expansion;
            MutableTag = toClone.MutableTag;
        }

        protected SubjectInfo(SubjectInfo toClone)
        {
            Key = toClone.Key;
            Expansion = toClone.Expansion;
            MutableTag = toClone.MutableTag;
        }

        protected SubjectInfo(BaseBuilder builder)
        {
            Key = builder.Key;

            if (builder.Expansion != null)
            {
                Expansion = builder.Expansion.ToImmutable();
            }

            MutableTag = builder.MutableTag;
        }

        public IIdentificator Key { get; }

        public string Name => Key.Name;

        public IExpansion? Expansion { get; private set; }

        public object? MutableTag { get; set; }

        public abstract IEnumerable<ISubjectInfo> GetChildren();

        public bool Equals(ISubjectInfo? other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            if (ReferenceEquals(other, this))
            {
                return true;
            }

            return Key.Equals(other.Key);
        }

        public bool Equals(IIdentificator? key)
        {
            return Key.Equals(key);
        }

        public bool Equals(string? key)
        {
            return Key.Equals(key);
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

            if (obj is ISubjectInfo subject)
            {
                return Equals(subject);
            }

            if (obj is IIdentificator indicator)
            {
                return Equals(indicator);
            }

            if (obj is string key)
            {
                return Equals(key);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return Key.GetHashCode();
        }

        protected TClone SetExpansion<TClone>(TClone clone, IExpansion expansion)
            where TClone : SubjectInfo
        {
            clone.Expansion = expansion;
            return clone;
        }
    }
}
