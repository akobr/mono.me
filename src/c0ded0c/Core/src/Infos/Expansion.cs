using System;
using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public sealed partial class Expansion : IExpansion
    {
        public static readonly Expansion Empty = new Expansion();

        private ImmutableDictionary<string, object> properties;
        private ImmutableHashSet<string> flags;

        public Expansion()
        {
            properties = ImmutableDictionary<string, object>.Empty.WithComparers(StringComparer.OrdinalIgnoreCase);
            flags = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.OrdinalIgnoreCase);
        }

        public Expansion(IExpansion toClone)
        {
            properties = ImmutableDictionary.CreateRange(StringComparer.OrdinalIgnoreCase, toClone.Properties);
            flags = ImmutableHashSet.CreateRange(StringComparer.OrdinalIgnoreCase, toClone.Flags);
        }

        private Expansion(Expansion toClone)
        {
            properties = toClone.properties;
            flags = toClone.flags;
        }

        private Expansion(Builder builder)
        {
            properties = builder.Properties.ToImmutable();
            flags = builder.Flags.ToImmutable();
        }

        public IImmutableDictionary<string, object> Properties => properties;

        public IImmutableSet<string> Flags => flags;

        public TProp GetProperty<TProp>(string key)
        {
            if (!properties.TryGetValue(key, out object value))
            {
                return default;
            }

            return (TProp)value;
        }

        public Expansion SetProperty(string key, object value)
        {
            return new Expansion(this)
            {
                properties = value == null
                    ? properties.Remove(key)
                    : properties.Remove(key).SetItem(key, value),
            };
        }

        public Expansion RemoveProperty(string key)
        {
            return new Expansion(this)
            {
                properties = properties.Remove(key),
            };
        }

        public Expansion SetFlag(string flag)
        {
            return new Expansion(this)
            {
                flags = flags.Add(flag),
            };
        }

        public Expansion RemoveFlag(string flag)
        {
            return new Expansion(this)
            {
                flags = flags.Remove(flag),
            };
        }

        public Builder ToBuilder()
        {
            return new Builder(this);
        }

        public static Expansion From(IExpansion expansion)
        {
            return expansion is Expansion familiar
                ? familiar
                : new Expansion(expansion);
        }
    }
}
