using System;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace c0ded0c.Core.Genesis
{
    public class GenesisEngine : BaseEngine<IWorkspaceInfo, IWorkspaceInfo>, IGenesisEngine
    {
        private static Func<IWorkspaceInfo, Task<IWorkspaceInfo>> dumpMiddleware =
            (workspace) => Task.FromResult<IWorkspaceInfo>(workspace);

        public GenesisEngine(
            IImmutableList<Func<IWorkspaceInfo, Func<IWorkspaceInfo, Task<IWorkspaceInfo>>, Task<IWorkspaceInfo>>> middlewares,
            IImmutableDictionary<string, string> properties)
            : base(middlewares, dumpMiddleware, properties)
        {
        }

        public Task<IWorkspaceInfo> ShapeAsync(IWorkspaceInfo workspace)
        {
            return Run(workspace);
        }
    }
}
