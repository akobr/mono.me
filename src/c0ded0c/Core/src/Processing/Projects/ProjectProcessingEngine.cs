using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class ProjectProcessingEngine : BaseEngine<IProjectInfo, IProjectInfo>, IProjectProcessingEngine
    {
        private static Func<IProjectInfo, Task<IProjectInfo>> dumpMiddleware = (project) => Task.FromResult(project);

        public ProjectProcessingEngine(
            IImmutableList<Func<IProjectInfo, Func<IProjectInfo, Task<IProjectInfo>>, Task<IProjectInfo>>> middlewares,
            IImmutableDictionary<string, string> properties)
            : base(middlewares, dumpMiddleware, properties)
        {
        }

        public Task<IProjectInfo> ProcessAsync(IProjectInfo project, CancellationToken cancellation = default)
        {
            return Run(project);
        }
    }
}
