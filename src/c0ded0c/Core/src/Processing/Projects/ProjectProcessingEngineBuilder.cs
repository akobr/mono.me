using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class ProjectProcessingEngineBuilder : BaseEngineBuilder<IProjectInfo, IProjectInfo, IProjectProcessingMiddleware>, IProjectProcessingEngineBuilder
    {
        public ProjectProcessingEngineBuilder(IServiceProvider services)
            : base(services)
        {
        }

        public async Task<IProjectProcessingEngine> BuildAsync(IImmutableDictionary<string, string> properties, CancellationToken cancelation = default)
        {
            await Task.Run(() => BuildMiddlewareInstancies(properties));
            return new ProjectProcessingEngine(GetMiddlewares(), properties);
        }

        protected override Func<IProjectInfo, Func<IProjectInfo, Task<IProjectInfo>>, Task<IProjectInfo>> BuildMiddlewareFunc(Lazy<IProjectProcessingMiddleware> instance)
        {
            return (project, next) =>
            {
                return instance.Value.ProcessAsync(project, new ProjectProcessingAsyncDelegate(next));
            };
        }
    }
}
