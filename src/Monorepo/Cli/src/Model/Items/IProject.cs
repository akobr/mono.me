using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Records;

namespace _42.Monorepo.Cli.Model.Items
{
    public interface IProject : IItem
    {
        new IProjectRecord Record { get; }

        Task<IReadOnlyCollection<IInternalDependency>> GetInternalDependenciesAsync(CancellationToken cancellationToken = default);
    }
}
