using System.IO;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli.Operations.Strategies
{
    public class VersionFilePathOpStrategy : IOpStrategy<string?>
    {
        public async Task<string?> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            var directory = item.Record.Path;
            string filePath = Path.Combine(directory, Constants.VERSION_FILE_NAME);

            return File.Exists(filePath)
                ? filePath
                : item.Parent is null
                    ? null
                    : await item.Parent.TryGetVersionFilePathAsync(cancellationToken);
        }
    }
}
