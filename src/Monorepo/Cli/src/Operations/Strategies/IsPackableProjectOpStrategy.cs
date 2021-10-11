using System.IO;
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
        private readonly IFileContentCache fileCache;

        public IsPackableProjectOpStrategy(IFileContentCache fileCache)
        {
            this.fileCache = fileCache;
        }

        public async Task<bool> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            if (item.Record.Type != ItemType.Project)
            {
                return false;
            }

            string projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item);

            if (!File.Exists(projectFilePath))
            {
                return false;
            }

            var xContent = await fileCache.GetOrLoadXmlContentAsync(projectFilePath, cancellationToken);

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
