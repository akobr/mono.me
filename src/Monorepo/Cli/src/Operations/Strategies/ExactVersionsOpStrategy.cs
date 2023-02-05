using System;
using System.IO.Abstractions;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using Nerdbank.GitVersioning;
using Semver;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class ExactVersionsOpStrategy : IOpStrategy<IExactVersions>
    {
        private readonly IFileSystem _fileSystem;
        private readonly IItemOptionsProvider _optionsProvider;
        private readonly IFileContentCache _fileCache;

        public ExactVersionsOpStrategy(
            IFileSystem fileSystem,
            IItemOptionsProvider optionsProvider,
            IFileContentCache fileCache)
        {
            _fileSystem = fileSystem;
            _optionsProvider = optionsProvider;
            _fileCache = fileCache;
        }

        public Task<IExactVersions> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            var options = _optionsProvider.GetOptions(item.Record.Path);

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
                SemVersion = packageVersion,
                AssemblyVersion = oracle.AssemblyVersion,
                AssemblyFileVersion = oracle.AssemblyFileVersion,
                AssemblyInformationalVersion = oracle.AssemblyInformationalVersion,
                PackageVersion = packageVersion,
            };
        }

        private async Task<IExactVersions> TryCalculateVersionFromProjectFileAsync(IItem item, CancellationToken cancellationToken)
        {
            if (item.Record.Type != RecordType.Project)
            {
                return new ExactVersions(new Version(1, 0));
            }

            var projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item, _fileSystem);

            if (!_fileSystem.File.Exists(projectFilePath))
            {
                return new ExactVersions(new Version(1, 0));
            }

            var xContent = await _fileCache.GetOrLoadXmlContentAsync(projectFilePath, cancellationToken);

            if (xContent.Root is null)
            {
                return new ExactVersions(new Version(1, 0));
            }

            var @namespace = xContent.Root.GetDefaultNamespace();
            var versionElement = xContent.Descendants(@namespace + "Version").FirstOrDefault();
            var versionPrefix = xContent.Descendants(@namespace + "VersionPrefix").FirstOrDefault();
            var versionSuffix = xContent.Descendants(@namespace + "VersionSuffix").FirstOrDefault();
            var assemblyVersionElement = xContent.Descendants(@namespace + "AssemblyVersion").FirstOrDefault();
            var fileVersionElement = xContent.Descendants(@namespace + "FileVersion").FirstOrDefault();
            var informationalVersionElement = xContent.Descendants(@namespace + "InformationalVersion").FirstOrDefault()
                                              ?? xContent.Descendants(@namespace + "AssemblyInformationalVersion").FirstOrDefault();
            var packageVersionElement = xContent.Descendants(@namespace + "PackageVersion").FirstOrDefault();

            SemVersion version = new(1, 0);
            Version assemblyVersion;
            Version fileVersion;
            string informationalVersion;
            SemVersion packageVersion;

            if (versionElement is not null
                && SemVersion.TryParse(versionElement.Value, out var parsedSemVersion))
            {
                // TODO: [P2] should support format major.minor.patch[.build][-prerelease]
                version = parsedSemVersion;
            }
            else if (versionPrefix is not null
                     && Version.TryParse(versionPrefix.Value, out var parsedPrefix))
            {
                version = new SemVersion(parsedPrefix);

                if (versionSuffix is not null
                    && Regex.IsMatch(versionSuffix.Value, "[0-9A-Za-z-]*"))
                {
                    version = version.Change(prerelease: versionSuffix.Value);
                }
            }

            if (assemblyVersionElement is not null
                && Version.TryParse(assemblyVersionElement.Value, out var parsedVersion))
            {
                assemblyVersion = parsedVersion;
            }
            else
            {
                assemblyVersion = new Version(version.Major, version.Minor, version.Patch, int.TryParse(version.Build, out var parsedBuild) ? parsedBuild : 0);
            }

            if (fileVersionElement is not null
                && Version.TryParse(fileVersionElement.Value, out parsedVersion))
            {
                fileVersion = parsedVersion;
            }
            else
            {
                fileVersion = (Version)assemblyVersion.Clone();
            }

            if (informationalVersionElement is not null
                && !string.IsNullOrWhiteSpace(informationalVersionElement.Value))
            {
                informationalVersion = informationalVersionElement.Value;
            }
            else
            {
                // TODO: [P3] should be in format major.minor.patch[.build][-prerelease]
                // but semantic version is major.minor.patch[-prerelease][+build]
                informationalVersion = version.ToString();
            }

            if (packageVersionElement is not null
                && SemVersion.TryParse(packageVersionElement.Value, out parsedSemVersion))
            {
                // TODO: [P2] should support format major.minor.patch[.build][-prerelease]
                packageVersion = parsedSemVersion;
            }
            else
            {
                packageVersion = version.Change();
            }

            return new ExactVersions
            {
                SemVersion = version,
                Version = version.ToVersion(),
                AssemblyVersion = assemblyVersion,
                AssemblyFileVersion = fileVersion,
                AssemblyInformationalVersion = informationalVersion,
                PackageVersion = packageVersion,
            };
        }
    }
}
