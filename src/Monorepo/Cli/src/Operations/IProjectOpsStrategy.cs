using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations
{
    public interface IProjectOpsStrategy
    {
        Task<IReadOnlyCollection<IInternalDependency>> CalculateInternalDependenciesAsync(IItem item, CancellationToken cancellationToken);
    }
}
