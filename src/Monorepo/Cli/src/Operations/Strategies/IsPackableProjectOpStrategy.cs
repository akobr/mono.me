using System.IO.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Cache;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class IsPackableProjectOpStrategy : IOpStrategy<bool>
    {
        private readonly IFileSystem _fileSystem;
        private readonly IFileContentCache _fileCache;

        public IsPackableProjectOpStrategy(
            IFileSystem fileSystem,
            IFileContentCache fileCache)
        {
            _fileSystem = fileSystem;
            _fileCache = fileCache;
        }

        public async Task<bool> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            if (item.Record.Type != RecordType.Project)
            {
                return false;
            }

            var projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item, _fileSystem);

            if (!_fileSystem.File.Exists(projectFilePath))
            {
                return false;
            }

            var xContent = await _fileCache.GetOrLoadXmlContentAsync(projectFilePath, cancellationToken);

            if (xContent.Root is null)
            {
                return false;
            }

            var @namespace = xContent.Root.GetDefaultNamespace();
            var packageNameElement = xContent.Descendants(@namespace + "IsPackable").FirstOrDefault();

            return !bool.TryParse(packageNameElement?.Value, out var parsedValue) || parsedValue;
        }
    }
}
