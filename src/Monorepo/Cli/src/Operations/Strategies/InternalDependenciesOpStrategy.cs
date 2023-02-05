using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class InternalDependenciesOpStrategy : IOpStrategy<IReadOnlyCollection<IInternalDependency>>
    {
        private readonly IFileSystem _fileSystem;
        private readonly IFileContentCache _fileCache;

        public InternalDependenciesOpStrategy(
            IFileSystem fileSystem,
            IFileContentCache fileCache)
        {
            _fileSystem = fileSystem;
            _fileCache = fileCache;
        }

        public async Task<IReadOnlyCollection<IInternalDependency>> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            if (item.Record.Type != RecordType.Project)
            {
                return Array.Empty<IInternalDependency>();
            }

            var projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item, _fileSystem);

            if (!_fileSystem.File.Exists(projectFilePath))
            {
                return Array.Empty<IInternalDependency>();
            }

            var projectDirectory = _fileSystem.Path.GetDirectoryName(projectFilePath)!;
            var xContent = await _fileCache.GetOrLoadXmlContentAsync(projectFilePath, cancellationToken);

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

                var fullPath = _fileSystem.Path.GetFullPath(_fileSystem.Path.Combine(projectDirectory, relativePath));
                var projectRepoPath = fullPath.GetRelativePath(repository.Path);
                var projectName = _fileSystem.Path.GetFileName(projectRepoPath);
                references.Add(new InternalDependency(projectName, projectRepoPath, fullPath));
            }

            return references;
        }
    }
}
