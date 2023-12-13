using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using Semver;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class ExternalDependenciesOpStrategy : IOpStrategy<IReadOnlyCollection<IExternalDependency>>
    {
        private readonly IFileSystem _fileSystem;
        private readonly IFileContentCache _fileCache;

        public ExternalDependenciesOpStrategy(
            IFileSystem fileSystem,
            IFileContentCache fileCache)
        {
            _fileSystem = fileSystem;
            _fileCache = fileCache;
        }

        public Task<IReadOnlyCollection<IExternalDependency>> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            return item.Record.Type != RecordType.Project
                ? CalculateGlobalExternalDependenciesAsync(item, cancellationToken)
                : CalculateExternalDependenciesForProjectAsync(item, cancellationToken);
        }

        // TODO: [P3] refactor this method
        private async Task<IReadOnlyCollection<IExternalDependency>> CalculateGlobalExternalDependenciesAsync(IItem item, CancellationToken cancellationToken)
        {
            var directory = item.Record.Path;
            var filePath = _fileSystem.Path.Combine(directory, Constants.PACKAGES_FILE_NAME);
            var parentTask = item.Parent is null
                ? Task.FromResult<IReadOnlyCollection<IExternalDependency>>(Array.Empty<IExternalDependency>())
                : item.Parent.GetExternalDependenciesAsync(cancellationToken);

            if (!_fileSystem.File.Exists(filePath))
            {
                return await parentTask;
            }

            var parentDependencies = await parentTask;
            var map = parentDependencies
                .ToDictionary(d => d.Name, d => new ExternalDependency(d.Name, d.Version));

            var xContent = await _fileCache.GetOrLoadXmlContentAsync(filePath, cancellationToken);

            if (xContent.Root is null)
            {
                return await parentTask;
            }

            // TODO: [P2] PackageVersion.Include or PackageReference.Update
            foreach (var xReference in xContent.Descendants(xContent.Root.GetDefaultNamespace() + "PackageVersion"))
            {
                var name = xReference.Attribute("Include")?.Value;
                var stringVersion = xReference.Attribute("Version")?.Value;

                if (name is not null && stringVersion is not null
                                     && SemVersion.TryParse(stringVersion, out var version))
                {
                    map[name] = new ExternalDependency(name, version) { IsDirect = true };
                }
            }

            return map
                .Select(i => i.Value)
                .ToList();
        }

        // TODO: [P3] refactor this method
        private async Task<IReadOnlyCollection<IExternalDependency>> CalculateExternalDependenciesForProjectAsync(IItem item, CancellationToken cancellationToken)
        {
            var projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item, _fileSystem);

            if (!_fileSystem.File.Exists(projectFilePath))
            {
                return Array.Empty<IExternalDependency>();
            }

            var dependencies = await CalculateGlobalExternalDependenciesAsync(item, cancellationToken);
            var map = new Dictionary<string, ExternalDependency>();

            foreach (var dependency in dependencies)
            {
                map[dependency.Name] = new ExternalDependency(dependency.Name, dependency.Version);
            }

            var xContent = await _fileCache.GetOrLoadXmlContentAsync(projectFilePath, cancellationToken);

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

                var version = map.TryGetValue(packageName, out var dependency) ? dependency.Version : new SemVersion(1);
                map[packageName] = new ExternalDependency(packageName, version) { IsDirect = true };
            }

            return map.Values;
        }
    }
}
