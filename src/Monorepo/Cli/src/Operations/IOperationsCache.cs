using System.Collections.Generic;
using _42.Monorepo.Cli.Model;
using Semver;

namespace _42.Monorepo.Cli.Operations
{
    public interface IOperationsCache
    {
        public bool TryGetInternalDependencies(IIdentifier itemKey, out IReadOnlyCollection<IInternalDependency> dependencies);

        public void StoreInternalDependencies(IIdentifier itemKey, IReadOnlyCollection<IInternalDependency> dependencies);

        public bool TryGetExternalDependencies(IIdentifier itemKey, out IReadOnlyCollection<IExternalDependency> dependencies);

        public void StoreExternalDependencies(IIdentifier itemKey, IReadOnlyCollection<IExternalDependency> dependencies);

        public bool TryGetAllReleases(IIdentifier itemKey, out IReadOnlyList<IRelease> releases);

        public void StoreAllReleases(IIdentifier itemKey, IReadOnlyList<IRelease> releases);

        public bool TryGetLastRelease(IIdentifier itemKey, out IRelease? release);

        public void StoreLastRelease(IIdentifier itemKey, IRelease? release);

        public bool TryGetVersionFilePath(IIdentifier itemKey, out string? versionFilePath);

        public void StoreVersionFilePath(IIdentifier itemKey, string? versionFilePath);

        public bool TryGetDefinedVersion(IIdentifier itemKey, out SemVersion? version);

        public void StoreDefinedVersion(IIdentifier itemKey, SemVersion? version);

        public bool TryGetExactVersion(IIdentifier itemKey, out SemVersion version);

        public void StoreExactVersion(IIdentifier itemKey, SemVersion version);
    }
}
