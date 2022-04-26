using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public interface IWorkspaceInfo : ISubjectInfo
    {
        /// <summary>
        /// Gets the map of all loaded assemblies indexed by assembly full name.
        /// </summary>
        [Artifact]
        IImmutableDictionary<string, IAssemblyInfo> Assemblies { get; }
    }
}
