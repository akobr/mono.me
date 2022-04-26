using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core.Genesis
{
    public class GenesisEngineBuilder : BaseEngineBuilder<IWorkspaceInfo, IWorkspaceInfo, IGenesisMiddleware>, IGenesisEngineBuilder
    {
        public GenesisEngineBuilder(IServiceProvider services)
            : base(services)
        {
        }

        public async Task<IGenesisEngine> BuildAsync(IImmutableDictionary<string, string> properties, CancellationToken cancelation = default)
        {
            await Task.Run(() => BuildMiddlewareInstancies(properties));
            return new GenesisEngine(GetMiddlewares(), properties);
        }

        protected override Func<IWorkspaceInfo, Func<IWorkspaceInfo, Task<IWorkspaceInfo>>, Task<IWorkspaceInfo>> BuildMiddlewareFunc(Lazy<IGenesisMiddleware> instance)
        {
            return (workspace, next) =>
            {
                return instance.Value.ShapeAsync(workspace, new GenesisAsyncDelegate(next));
            };
        }
    }
}
