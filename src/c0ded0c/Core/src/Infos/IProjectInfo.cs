using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public interface IProjectInfo : ISubjectInfo
    {
        [InfoProperty]
        string Path { get; }

        [InfoProperty]
        bool IsAggregated { get; }

        IAssemblyInfo? Assembly { get; }

        /// <summary>
        /// Gets the map of sub-projects inside the project indexed by relative path.
        /// </summary>
        [Artifact]
        IImmutableDictionary<string, IProjectInfo> Projects { get; }
    }
}
