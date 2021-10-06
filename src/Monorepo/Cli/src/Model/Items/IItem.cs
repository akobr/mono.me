using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Records;
using Semver;

namespace _42.Monorepo.Cli.Model.Items
{
    public interface IItem : IEquatable<IItem>
    {
        IRecord Record { get; }

        IItem? Parent { get; }

        IEnumerable<IItem> GetChildren();

        Task<string?> TryGetVersionFilePathAsync(CancellationToken cancellationToken = default);

        Task<SemVersion?> TryGetDefinedVersionAsync(CancellationToken cancellationToken = default);

        Task<IExactVersions> GetExactVersionsAsync(CancellationToken cancellationToken = default);

        Task<IRelease?> TryGetLastReleaseAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<IRelease>> GetAllReleasesAsync(CancellationToken cancellationToken = default);

        Task<string?> TryGetPackagesFilePathAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<IExternalDependency>> GetExternalDependenciesAsync(CancellationToken cancellationToken = default);
    }
}
