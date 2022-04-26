using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core
{
    public class AssemblyProcessingEngineBuilder : BaseEngineBuilder<IAssemblyInfo, IAssemblyInfo, IAssemblyProcessingMiddleware>, IAssemblyProcessingEngineBuilder
    {
        public AssemblyProcessingEngineBuilder(IServiceProvider services)
            : base(services)
        {
        }

        public async Task<IAssemblyProcessingEngine> BuildAsync(IImmutableDictionary<string, string> properties, CancellationToken cancelation = default)
        {
            await Task.Run(() => BuildMiddlewareInstancies(properties));
            return new AssemblyProcessingEngine(GetMiddlewares(), properties);
        }

        protected override Func<IAssemblyInfo, Func<IAssemblyInfo, Task<IAssemblyInfo>>, Task<IAssemblyInfo>> BuildMiddlewareFunc(Lazy<IAssemblyProcessingMiddleware> instance)
        {
            return (assembly, next) =>
            {
                return instance.Value.ProcessAsync(assembly, new AssemblyProcessingAsyncDelegate(next));
            };
        }
    }
}
