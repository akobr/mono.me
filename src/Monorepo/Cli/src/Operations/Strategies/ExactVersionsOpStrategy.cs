using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using Nerdbank.GitVersioning;
using Semver;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class ExactVersionsOpStrategy : IOpStrategy<IExactVersions>
    {
        private readonly IItemOptionsProvider optionsProvider;
        private readonly IFileContentCache fileCache;

        public ExactVersionsOpStrategy(IItemOptionsProvider optionsProvider, IFileContentCache fileCache)
        {
            this.optionsProvider = optionsProvider;
            this.fileCache = fileCache;
        }

        public Task<IExactVersions> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            var options = optionsProvider.TryGetOptions(item.Record.Path);

            if (options.IsVersioned())
            {
                return Task.FromResult(CalculateGitVersion(item));
            }

            return TryCalculateVersionFromProjectFileAsync(item, cancellationToken);
        }

        private static IExactVersions CalculateGitVersion(IItem item)
        {
            using var context = GitContext.Create(item.Record.Path);
            var oracle = new VersionOracle(context, CloudBuild.Active);

            // Take the PublicRelease environment variable into account, since the build would as well.
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PublicRelease"))
                && bool.TryParse(Environment.GetEnvironmentVariable("PublicRelease"), out var publicRelease))
            {
                oracle.PublicRelease = publicRelease;
            }

            var packageVersion = SemVersion.TryParse(oracle.SemVer2, out var parsedVersion)
                ? parsedVersion
                : new SemVersion(oracle.Version);

            return new ExactVersions
            {
                Version = oracle.Version,
                AssemblyVersion = oracle.AssemblyVersion,
                AssemblyInformationalVersion = oracle.AssemblyInformationalVersion,
                PackageVersion = packageVersion,
            };
        }

        private async Task<IExactVersions> TryCalculateVersionFromProjectFileAsync(IItem item, CancellationToken cancellationToken)
        {
            if (item.Record.Type != ItemType.Project)
            {
                return new ExactVersions(new Version(1, 0));
            }

            string projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item);

            if (!File.Exists(projectFilePath))
            {
                return new ExactVersions(new Version(1, 0));
            }

            var xContent = await fileCache.GetOrLoadXmlContentAsync(projectFilePath, cancellationToken);

            if (xContent.Root is null)
            {
                return new ExactVersions(new Version(1, 0));
            }

            var @namespace = xContent.Root.GetDefaultNamespace();
            var versionElement = xContent.Descendants(@namespace + "Version").FirstOrDefault();
            var assemblyVersionElement = xContent.Descendants(@namespace + "AssemblyVersion").FirstOrDefault();
            var informationalVersionElement = xContent.Descendants(@namespace + "InformationalVersion").FirstOrDefault()
                                              ?? xContent.Descendants(@namespace + "AssemblyInformationalVersion").FirstOrDefault();
            var packageVersionElement = xContent.Descendants(@namespace + "PackageVersion").FirstOrDefault();

            Version version = new(1, 0);
            Version assemblyVersion;
            SemVersion informationalVersion;
            SemVersion packageVersion;

            if (versionElement is not null
                && Version.TryParse(versionElement.Value, out var parsedVersion))
            {
                version = parsedVersion;
            }

            if (assemblyVersionElement is not null
                && Version.TryParse(assemblyVersionElement.Value, out parsedVersion))
            {
                assemblyVersion = parsedVersion;
            }
            else
            {
                assemblyVersion = version;
            }

            if (informationalVersionElement is not null
                && SemVersion.TryParse(informationalVersionElement.Value, out var parsedSemVersion))
            {
                informationalVersion = parsedSemVersion;
            }
            else
            {
                informationalVersion = new SemVersion(version);
            }

            if (packageVersionElement is not null
                && SemVersion.TryParse(packageVersionElement.Value, out parsedSemVersion))
            {
                packageVersion = parsedSemVersion;
            }
            else
            {
                packageVersion = new SemVersion(version);
            }

            return new ExactVersions
            {
                Version = version,
                AssemblyVersion = assemblyVersion,
                AssemblyInformationalVersion = informationalVersion.ToString(),
                PackageVersion = packageVersion,
            };
        }
    }
}
