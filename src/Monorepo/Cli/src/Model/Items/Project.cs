using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model.Records;
using _42.Monorepo.Cli.Operations;
using Semver;

namespace _42.Monorepo.Cli.Model.Items
{
    public class Project : Item, IProject
    {
        private readonly Lazy<string> projectFile;
        private readonly AsyncLazy<IReadOnlyCollection<IInternalDependency>> internalDependencies;

        public Project(IProjectRecord item, IOperationsCache cache, Func<IItemRecord, IItem> itemFactory)
            : base(item, cache, itemFactory)
        {
            Record = item;

            projectFile = new(CalculateProjectFile, true);
            internalDependencies = new(
                t => ProcessOperation(cache.TryGetInternalDependencies, CalculateInternalDependenciesAsync, cache.StoreInternalDependencies, t));
        }

        public new IProjectRecord Record { get; }

        public Task<IReadOnlyCollection<IInternalDependency>> GetInternalDependenciesAsync(CancellationToken cancellationToken = default)
            => internalDependencies.GetValueAsync(cancellationToken);

        // TODO: [P3] refactor this method
        protected override async Task<IReadOnlyCollection<IExternalDependency>> CalculateExternalDependenciesAsync(CancellationToken cancellationToken)
        {
            string projectFilePath = projectFile.Value;

            if (!File.Exists(projectFilePath))
            {
                return Array.Empty<IExternalDependency>();
            }

            var dependencies = await base.CalculateExternalDependenciesAsync(cancellationToken);
            var map = dependencies.ToDictionary(d => d.Name, d => d.Version);
            var localMap = new Dictionary<string, SemVersion>();
            var xContent = await XDocument.LoadAsync(File.OpenText(projectFilePath), LoadOptions.None, cancellationToken);

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

        // TODO: [P3] refactor this method
        private async Task<IReadOnlyCollection<IInternalDependency>> CalculateInternalDependenciesAsync(CancellationToken cancellationToken)
        {
            string projectFilePath = projectFile.Value;

            if (!File.Exists(projectFilePath))
            {
                return Array.Empty<IInternalDependency>();
            }

            var xContent = await XDocument.LoadAsync(File.OpenText(projectFilePath), LoadOptions.None, cancellationToken);

            if (xContent.Root is null)
            {
                return Array.Empty<IInternalDependency>();
            }

            var repository = Record.TryGetConcreteItem(ItemType.Repository);
            List<IInternalDependency> references = new();

            if (repository is null)
            {
                // TODO: logging
                Console.WriteLine("no repository found");
                return Array.Empty<IInternalDependency>();
            }

            foreach (var xReference in xContent.Descendants(xContent.Root.GetDefaultNamespace() + "ProjectReference"))
            {
                var relativePath = xReference.Attribute("Include")?.Value;

                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                string fullPath = Path.Combine(Record.Path, relativePath);
                string projectRepoPath = fullPath.GetRelativePath(repository.Path);
                string projectName = Path.GetFileName(projectRepoPath);
                references.Add(new InternalDependency(projectName, projectRepoPath));
            }

            return references;
        }

        private string CalculateProjectFile()
        {
            var sourceDirectoryPath = Path.Combine(Record.Path, Constants.SOURCE_DIRECTORY_NAME);
            var projectFiles = Directory.GetFiles(sourceDirectoryPath, "*.csproj", SearchOption.TopDirectoryOnly);
            return projectFiles.Length > 0 ? projectFiles[0] : string.Empty;
        }
    }
}
