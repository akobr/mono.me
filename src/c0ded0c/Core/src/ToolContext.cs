using System.Collections.Concurrent;
using System.Collections.Generic;

namespace c0ded0c.Core
{
    public class ToolContext
    {
        private readonly ConcurrentDictionary<IIdentificator, ProjectInfo> projects;
        private readonly ConcurrentDictionary<IIdentificator, AssemblyInfo> assemblies;

        public ToolContext()
        {
            projects = new ConcurrentDictionary<IIdentificator, ProjectInfo>();
            assemblies = new ConcurrentDictionary<IIdentificator, AssemblyInfo>();
        }

        public IReadOnlyDictionary<IIdentificator, ProjectInfo> Projects => projects;

        public IReadOnlyDictionary<IIdentificator, AssemblyInfo> Assemblies => assemblies;
    }
}
