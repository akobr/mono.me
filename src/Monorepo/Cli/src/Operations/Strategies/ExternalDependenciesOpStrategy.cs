using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using Semver;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class ExternalDependenciesOpStrategy : IOpStrategy<IReadOnlyCollection<IExternalDependency>>
    {
        private readonly IFileContentCache fileCache;

        public ExternalDependenciesOpStrategy(IFileContentCache fileCache)
        {
            this.fileCache = fileCache;
        }

        public Task<IReadOnlyCollection<IExternalDependency>> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            return item.Record.Type != ItemType.Project
                ? CalculateGlobalExternalDependenciesAsync(item, cancellationToken)
                : CalculateExternalDependenciesForProjectAsync(item, cancellationToken);
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
                .ToDictionary(d => d.Name, d => d.Version);

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
                    map[name] = version;
                }
            }

            return map
                .Select(i => new ExternalDependency(i.Key, i.Value))
                .ToList();
        }

        // TODO: [P3] refactor this method
        private async Task<IReadOnlyCollection<IExternalDependency>> CalculateExternalDependenciesForProjectAsync(IItem item, CancellationToken cancellationToken)
        {
            string projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item);

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
                .Select(i => new ExternalDependency(i.Key, i.Value))
                .ToList();
        }
    }
}
