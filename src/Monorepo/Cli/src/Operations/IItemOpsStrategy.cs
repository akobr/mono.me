using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using Semver;

namespace _42.Monorepo.Cli.Operations
{
    public interface IItemOpsStrategy
    {
        Task<string?> CalculateVersionFilePathAsync(IItem item, CancellationToken cancellationToken);

        Task<SemVersion?> CalculateDefinedVersionAsync(IItem item, CancellationToken cancellationToken);

        Task<IExactVersions> CalculateExactVersionsAsync(IItem item, CancellationToken cancellationToken);

        Task<IReadOnlyList<IRelease>> CalculateAllReleasesAsync(IItem item, CancellationToken cancellationToken);

        Task<IRelease?> CalculateLastReleaseAsync(IItem item, CancellationToken cancellationToken);

        Task<string?> CalculatePackagesFilePathAsync(IItem item, CancellationToken cancellationToken);

        Task<IReadOnlyCollection<IExternalDependency>> CalculateExternalDependenciesAsync(IItem item, CancellationToken cancellationToken);
    }
}
