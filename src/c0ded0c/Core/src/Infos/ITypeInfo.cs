using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public interface ITypeInfo : ISubjectInfo
    {
        /// <summary>
        /// Gets the map of members inside the type indexed by in-type-identification: Name, arguments and return types.
        /// </summary>
        [Artifact]
        IImmutableDictionary<string, IMemberInfo> Members { get; }

        /// <summary>
        /// Gets the map of nested types inside the type indexed by short name.
        /// </summary>
        [Artifact]
        IImmutableDictionary<string, ITypeInfo> NestedTypes { get; }
    }
}
