using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public interface IExpansion
    {
        IImmutableDictionary<string, object> Properties { get; }

        IImmutableSet<string> Flags { get; }
    }
}
