using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations
{
    internal class EmptyOpsExecutor : IOpsExecutor
    {
        public Task<T> ExecuteAsync<T>(IItem item, string operationKey = "", CancellationToken cancellationToken = default)
        {
            return Task.FromResult<T>(default!);
        }

        public Task ExecuteAsync(IItem item, string operationKey = "", CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
