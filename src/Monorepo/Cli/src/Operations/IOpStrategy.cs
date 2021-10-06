using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations
{
    public interface IOpStrategyGeneric
    {
        // no member
    }

    public interface IOpStrategy<T> : IOpStrategyGeneric
    {
        Task<T> OperateAsync(IItem item, CancellationToken cancellationToken = default);
    }

    public interface IOpStrategy
    {
        Task OperateAsync(IItem item, CancellationToken cancellationToken = default);
    }
}
