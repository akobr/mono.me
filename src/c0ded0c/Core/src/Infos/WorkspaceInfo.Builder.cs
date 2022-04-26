using System.Collections.Immutable;

namespace c0ded0c.Core
{
    public partial class WorkspaceInfo
    {
        public sealed class Builder : BaseBuilder
        {
            internal Builder(WorkspaceInfo workspace)
            : base(workspace)
            {
                Assemblies = workspace.assemblies.ToBuilder();
            }

            public ImmutableDictionary<string, IAssemblyInfo>.Builder Assemblies { get; private set; }

            public void AddAssembly(IAssemblyInfo assembly)
            {
                Assemblies.Add(assembly.Key.FullName, assembly);
            }

            public WorkspaceInfo ToImmutable()
            {
                return new WorkspaceInfo(this);
            }
        }
    }
}
