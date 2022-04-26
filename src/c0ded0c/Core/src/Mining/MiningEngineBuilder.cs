using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace c0ded0c.Core.Mining
{
    public class MiningEngineBuilder : BaseEngineBuilder<string, IImmutableSet<IProjectInfo>, IMiningMiddleware>, IMiningEngineBuilder
    {
        public MiningEngineBuilder(IServiceProvider services)
            : base(services)
        {
        }

        public async Task<IMiningEngine> BuildAsync(IImmutableDictionary<string, string> properties, CancellationToken cancelation = default)
        {
            await Task.Run(() => BuildMiddlewareInstancies(properties));
            return new MiningEngine(GetMiddlewares(), properties);
        }

        protected override Func<string, Func<string, Task<IImmutableSet<IProjectInfo>>>, Task<IImmutableSet<IProjectInfo>>> BuildMiddlewareFunc(Lazy<IMiningMiddleware> instance)
        {
            return (path, next) =>
            {
                return instance.Value.MineAsync(path, new MiningAsyncDelegate(next));
            };
        }
    }
}
