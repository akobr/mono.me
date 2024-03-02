using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class PackageNameProjectOpStrategy : IOpStrategy<string>
    {
        private readonly IFileSystem _fileSystem;
        private readonly IFileContentCache _fileCache;

        public PackageNameProjectOpStrategy(
            IFileSystem fileSystem,
            IFileContentCache fileCache)
        {
            _fileSystem = fileSystem;
            _fileCache = fileCache;
        }

        public async Task<string> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            if (item.Record.Type != RecordType.Project)
            {
                return item.Record.Name;
            }

            var projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item, _fileSystem);

            if (!_fileSystem.File.Exists(projectFilePath))
            {
                return item.Record.Name;
            }

            var xContent = await _fileCache.GetOrLoadXmlContentAsync(projectFilePath, cancellationToken);

            if (xContent.Root is null)
            {
                return _fileSystem.Path.GetFileNameWithoutExtension(projectFilePath);
            }

            var @namespace = xContent.Root.GetDefaultNamespace();
            var packageNameElement = xContent.Descendants(@namespace + "PackageId").FirstOrDefault()
                                     ?? xContent.Descendants(@namespace + "AssemblyName").FirstOrDefault();

            return packageNameElement?.Value ?? _fileSystem.Path.GetFileNameWithoutExtension(projectFilePath);
        }
    }
}
