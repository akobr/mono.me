using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations
{
    public class OpsExecutor : IOpsExecutor
    {
        private readonly IGenericOpsCache cache;
        private readonly IOpStrategiesFactory strategyFactory;

        public OpsExecutor(IGenericOpsCache cache, IOpStrategiesFactory strategyFactory)
        {
            this.cache = cache;
            this.strategyFactory = strategyFactory;
        }

        public Task<T> ExecuteAsync<T>(IItem item, [CallerMemberName] string operationKey = "", CancellationToken cancellationToken = default)
        {
            return cache
                .GetOrAddItem(item.Record.Identifier)
                .GetOrAddValue(
                    operationKey,
                    key => new AsyncLazy<T>(t => strategyFactory.BuildStrategy<T>(item, key).OperateAsync(item, t)))
                .GetValueAsync(cancellationToken);
        }

        public Task ExecuteAsync(IItem item, [CallerMemberName] string operationKey = "", CancellationToken cancellationToken = default)
        {
            return strategyFactory
                .BuildStrategy(item, operationKey)
                .OperateAsync(item, cancellationToken);
        }
    }
}
