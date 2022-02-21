using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly IFileContentCache fileCache;

        public InternalDependenciesOpStrategy(IFileContentCache fileCache)
        {
            this.fileCache = fileCache;
        }

        public async Task<IReadOnlyCollection<IInternalDependency>> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            if (item.Record.Type != RecordType.Project)
            {
                return Array.Empty<IInternalDependency>();
            }

            string projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item);

            if (!File.Exists(projectFilePath))
            {
                return Array.Empty<IInternalDependency>();
            }

            string projectDirectory = Path.GetDirectoryName(projectFilePath)!;
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

                string fullPath = Path.GetFullPath(Path.Combine(projectDirectory, relativePath));
                string projectRepoPath = fullPath.GetRelativePath(repository.Path);
                string projectName = Path.GetFileName(projectRepoPath);
                references.Add(new InternalDependency(projectName, projectRepoPath, fullPath));
            }

            return references;
        }
    }
}
