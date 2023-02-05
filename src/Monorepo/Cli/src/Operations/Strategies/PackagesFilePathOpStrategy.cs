using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class PackagesFilePathOpStrategy : IOpStrategy<string?>
    {
        private readonly IFileSystem _fileSystem;

        public PackagesFilePathOpStrategy(IFileSystem fileSystem)
        {
            _fileSystem = fileSystem;
        }

        public async Task<string?> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            var directory = item.Record.Path;
            var filePath = _fileSystem.Path.Combine(directory, Constants.PACKAGES_FILE_NAME);

            return _fileSystem.File.Exists(filePath)
                ? filePath
                : item.Parent is null
                    ? null
                    : await item.Parent.TryGetPackagesFilePathAsync(cancellationToken);
        }
    }
}
