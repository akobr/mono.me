using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Records;
using Semver;

namespace _42.Monorepo.Cli.Model.Items
{
    public interface IItem
    {
        IItemRecord Record { get; }

        IItem? Parent { get; }

        Task<string?> TryGetVersionFilePathAsync(CancellationToken cancellationToken = default);

        Task<SemVersion?> TryGetDefinedVersionAsync(CancellationToken cancellationToken = default);

        Task<SemVersion> GetExactVersionAsync(CancellationToken cancellationToken = default);

        Task<IRelease?> TryGetLastReleaseAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<IRelease>> GetAllReleasesAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<IExternalDependency>> GetExternalDependenciesAsync(CancellationToken cancellationToken = default);
    }
}
