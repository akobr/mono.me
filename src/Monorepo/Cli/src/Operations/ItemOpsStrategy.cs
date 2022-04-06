using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Versioning;
using Nerdbank.GitVersioning;
using Semver;

namespace _42.Monorepo.Cli.Operations
{
    public class ItemOpsStrategy : IItemOpsStrategy
    {
        private readonly IGitTagsService gitTagsService;
        private readonly IItemOptionsProvider optionsProvider;
        private readonly IFileContentCache fileCache;

        public ItemOpsStrategy(
            IGitTagsService gitTagsService,
            IItemOptionsProvider optionsProvider,
            IFileContentCache fileCache)
        {
            this.gitTagsService = gitTagsService;
            this.optionsProvider = optionsProvider;
            this.fileCache = fileCache;
        }

        public async Task<string?> CalculateVersionFilePathAsync(IItem item, CancellationToken cancellationToken)
        {
            var directory = item.Record.Path;
            var filePath = Path.Combine(directory, Constants.VERSION_FILE_NAME);

            return File.Exists(filePath)
                ? filePath
                : item.Parent is null
                    ? null
                    : await item.Parent.TryGetVersionFilePathAsync(cancellationToken);
        }

        // TODO: [P3] refactor this method
        public async Task<IVersionTemplate?> CalculateDefinedVersionAsync(IItem item, CancellationToken cancellationToken)
        {
            var directory = item.Record.Path;
            var filePath = Path.Combine(directory, Constants.VERSION_FILE_NAME);

            if (!File.Exists(filePath))
            {
                return item.Parent is null
                    ? null
                    : await item.Parent.TryGetDefinedVersionAsync(cancellationToken);
            }

            string? versionString = null;

            try
            {
                var versionDocument = await fileCache.GetOrLoadJsonContentAsync(filePath, cancellationToken);
                var rootElement = versionDocument.RootElement;
                versionString = rootElement
                    .GetProperty(Constants.VERSION_PROPERTY_NAME)
                    .GetString();
            }
            catch (JsonException e)
            {
                // TODO: logging
                Console.WriteLine($"Error in version.json: {e.Message}");
            }

            if (versionString is null
                || !VersionTemplate.TryParse(versionString, out var parsedVersion))
            {
                return item.Parent is null
                    ? null
                    : await item.Parent.TryGetDefinedVersionAsync(cancellationToken);
            }

            return parsedVersion;
        }

        public Task<IExactVersions> CalculateExactVersionsAsync(IItem item, CancellationToken cancellationToken)
        {
            var options = optionsProvider.GetOptions(item.Record.Path);

            if (options.IsVersioned())
            {
                return Task.FromResult(CalculateGitVersion(item));
            }

            return TryCalculateVersionFromProjectFileAsync(item, cancellationToken);
        }

        public async Task<IReadOnlyList<IRelease>> CalculateAllReleasesAsync(IItem item, CancellationToken cancellationToken)
        {
            var parentReleases = item.Parent is null
                ? Array.Empty<IRelease>()
                : await item.Parent.GetAllReleasesAsync(cancellationToken);

            var allReleases = parentReleases
                .Where(r => r.SubReleases.Any(sr => sr.Target.Identifier == item.Record.Identifier))
                .Select(r => r.SubReleases.First(sr => sr.Target.Identifier == item.Record.Identifier))
                .ToList();

            allReleases.AddRange(await Task.Run(() => GetExactReleases(item, cancellationToken), cancellationToken));
            allReleases.Sort(new ReleaseVersionComparer());
            return allReleases;
        }

        public async Task<IRelease?> CalculateLastReleaseAsync(IItem item, CancellationToken cancellationToken)
        {
            var allReleases = await item.GetAllReleasesAsync(cancellationToken);
            return allReleases.Count > 0 ? allReleases[0] : null;
        }

        public async Task<string?> CalculatePackagesFilePathAsync(IItem item, CancellationToken cancellationToken)
        {
            var directory = item.Record.Path;
            var filePath = Path.Combine(directory, Constants.PACKAGES_FILE_NAME);

            return File.Exists(filePath)
                ? filePath
                : item.Parent is null
                    ? null
                    : await item.Parent.TryGetPackagesFilePathAsync(cancellationToken);
        }

        public Task<IReadOnlyCollection<IExternalDependency>> CalculateExternalDependenciesAsync(IItem item, CancellationToken cancellationToken)
        {
            return item.Record.Type != RecordType.Project
                ? CalculateGlobalExternalDependenciesAsync(item, cancellationToken)
                : CalculateExternalDependenciesForProjectAsync(item, cancellationToken);
        }

        // TODO: [P3] refactor this method
        public async Task<IReadOnlyCollection<IInternalDependency>> CalculateInternalDependenciesAsync(IItem item, CancellationToken cancellationToken)
        {
            if (item.Record.Type != RecordType.Project)
            {
                return Array.Empty<IInternalDependency>();
            }

            var projectFilePath = CalculateProjectFile(item.Record.Path);

            if (!File.Exists(projectFilePath))
            {
                return Array.Empty<IInternalDependency>();
            }

            var projectDirectory = Path.GetDirectoryName(projectFilePath)!;
            var xContent = await fileCache.GetOrLoadXmlContentAsync(projectFilePath, cancellationToken);

            if (xContent.Root is null)
            {
                return Array.Empty<IInternalDependency>();
            }

            var repository = item.Record.TryGetConcreteItem(RecordType.Repository);
            List<IInternalDependency> references = new();

            if (repository is null)
            {
                return Array.Empty<IInternalDependency>();
            }

            foreach (var xReference in xContent.Descendants(xContent.Root.GetDefaultNamespace() + "ProjectReference"))
            {
                var relativePath = xReference.Attribute("Include")?.Value;

                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var fullPath = Path.GetFullPath(Path.Combine(projectDirectory, relativePath));
                var projectRepoPath = fullPath.GetRelativePath(repository.Path);
                var projectName = Path.GetFileName(projectRepoPath);
                references.Add(new InternalDependency(projectName, projectRepoPath, fullPath));
            }

            return references;
        }

        private async Task<IExactVersions> TryCalculateVersionFromProjectFileAsync(IItem item, CancellationToken cancellationToken)
        {
            if (item.Record.Type != RecordType.Project)
            {
                return new ExactVersions(new Version(1, 0));
            }

            var projectFilePath = CalculateProjectFile(item.Record.Path);

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
                SemVersion = packageVersion,
                AssemblyVersion = assemblyVersion,
                AssemblyInformationalVersion = informationalVersion.ToString(),
                PackageVersion = packageVersion,
            };
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
                AssemblyInformationalVersion = oracle.AssemblyInformationalVersion,
                PackageVersion = packageVersion,
            };
        }

        private List<IRelease> GetExactReleases(IItem item, CancellationToken cancellationToken)
        {
            string releasePrefix = $"{item.Record.Identifier.Humanized}/v.";
            var repository = item.Record.TryGetConcreteItem(RecordType.Repository) ?? MonorepoDirectoryFunctions.GetMonoRepository();

            List<IRelease> exactReleases = new();

            foreach (var tag in gitTagsService.GetTags())
            {
                if (tag.FriendlyName.StartsWith(releasePrefix, StringComparison.OrdinalIgnoreCase)
                    && Release.TryParse(tag, repository, out var release))
                {
                    exactReleases.Add(release);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            return exactReleases;
        }

        // TODO: [P3] refactor this method
        private async Task<IReadOnlyCollection<IExternalDependency>> CalculateGlobalExternalDependenciesAsync(IItem item, CancellationToken cancellationToken)
        {
            var directory = item.Record.Path;
            string filePath = Path.Combine(directory, Constants.PACKAGES_FILE_NAME);
            var parentTask = item.Parent is null
                ? Task.FromResult<IReadOnlyCollection<IExternalDependency>>(Array.Empty<IExternalDependency>())
                : item.Parent.GetExternalDependenciesAsync(cancellationToken);

            if (!File.Exists(filePath))
            {
                return await parentTask;
            }

            var parentDependencies = await parentTask;
            var map = parentDependencies
                .ToDictionary(d => d.Name, d => new ExternalDependency(d.Name, d.Version, false));

            XDocument xContent = await fileCache.GetOrLoadXmlContentAsync(filePath, cancellationToken);

            if (xContent.Root is null)
            {
                return await parentTask;
            }

            foreach (var xReference in xContent.Descendants(xContent.Root.GetDefaultNamespace() + "PackageReference"))
            {
                var name = xReference.Attribute("Update")?.Value;
                var stringVersion = xReference.Attribute("Version")?.Value;

                if (name is not null && stringVersion is not null
                                     && SemVersion.TryParse(stringVersion, out var version))
                {
                    map[name] = new ExternalDependency(name, version, true);
                }
            }

            return map
                .Select(i => i.Value)
                .ToList();
        }

        // TODO: [P3] refactor this method
        private async Task<IReadOnlyCollection<IExternalDependency>> CalculateExternalDependenciesForProjectAsync(IItem item, CancellationToken cancellationToken)
        {
            var projectFilePath = CalculateProjectFile(item.Record.Path);

            if (!File.Exists(projectFilePath))
            {
                return Array.Empty<IExternalDependency>();
            }

            var dependencies = await CalculateGlobalExternalDependenciesAsync(item, cancellationToken);
            var map = dependencies.ToDictionary(d => d.Name, d => d.Version);
            var localMap = new Dictionary<string, SemVersion>();
            var xContent = await fileCache.GetOrLoadXmlContentAsync(projectFilePath, cancellationToken);

            if (xContent.Root is null)
            {
                return Array.Empty<IExternalDependency>();
            }

            foreach (var xReference in xContent.Descendants(xContent.Root.GetDefaultNamespace() + "PackageReference"))
            {
                var packageName = xReference.Attribute("Include")?.Value;

                if (string.IsNullOrWhiteSpace(packageName))
                {
                    continue;
                }

                localMap[packageName] = map.TryGetValue(packageName, out var version) ? version : new SemVersion(1);
            }

            return localMap
                .Select(i => new ExternalDependency(i.Key, i.Value, true))
                .ToList();
        }

        private static string CalculateProjectFile(string projectPath)
        {
            var sourceDirectoryPath = Path.Combine(projectPath, Constants.SOURCE_DIRECTORY_NAME);

            if (!Directory.Exists(sourceDirectoryPath))
            {
                sourceDirectoryPath = projectPath;
            }

            var projectFiles = Directory.GetFiles(sourceDirectoryPath, "*.csproj", SearchOption.TopDirectoryOnly);
            return projectFiles.Length > 0 ? projectFiles[0] : string.Empty;
        }
    }
}
