using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations
{
    public interface IOpsExecutor
    {
        Task<T> ExecuteAsync<T>(IItem item, [CallerMemberName] string operationKey = "", CancellationToken cancellationToken = default);

        Task ExecuteAsync(IItem item, [CallerMemberName] string operationKey = "", CancellationToken cancellationToken = default);
    }
}
