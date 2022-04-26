using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public partial class Expansion
    {
        public sealed class Builder
        {
            internal Builder(Expansion expansion)
            {
                Properties = expansion.properties.ToBuilder();
                Flags = expansion.flags.ToBuilder();
            }

            public ImmutableDictionary<string, object>.Builder Properties { get; set; }

            public ImmutableHashSet<string>.Builder Flags { get; set; }

            public Expansion ToImmutable()
            {
                return new Expansion(this);
            }
        }
    }
}
