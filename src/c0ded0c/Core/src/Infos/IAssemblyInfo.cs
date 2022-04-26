using System.Collections.Immutable;
using Semver;

namespace c0ded0c.Core
{
    public interface IAssemblyInfo : ISubjectInfo
    {
        [InfoProperty]
        SemVersion Version { get; }

        /// <summary>
        /// Gets the map of namespaces inside the assembly indexed by full name.
        /// </summary
        [Artifact]
        IImmutableDictionary<string, INamespaceInfo> Namespaces { get; }

        /// <summary>
        /// Gets the map of types inside the assembly indexed by the full name.
        /// </summary>
        [Artifact]
        IImmutableDictionary<string, ITypeInfo> Types { get; }

        /// <summary>
        /// Gets the map of all members inside the assembly indexed by the full name.
        /// </summary>
        [Artifact]
        IImmutableDictionary<string, IMemberInfo> Members { get; }

        /// <summary>
        /// Gets the map of documents inside the assembly/project indexed by the relative path.
        /// </summary>
        [Artifact]
        IImmutableDictionary<string, IDocumentInfo> Documents { get; }
    }
}
