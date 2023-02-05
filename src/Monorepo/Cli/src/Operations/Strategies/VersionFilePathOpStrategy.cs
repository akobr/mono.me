using System.IO.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class VersionFilePathOpStrategy : IOpStrategy<string?>
    {
        private readonly IFileSystem _fileSystem;

        public VersionFilePathOpStrategy()
        {
            _fileSystem = new FileSystem();
        }

        public async Task<string?> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            var directory = item.Record.Path;
            var filePath = _fileSystem.Path.Combine(directory, Constants.VERSION_FILE_NAME);

            return _fileSystem.File.Exists(filePath)
                ? filePath
                : item.Parent is null
                    ? null
                    : await item.Parent.TryGetVersionFilePathAsync(cancellationToken);
        }
    }
}
