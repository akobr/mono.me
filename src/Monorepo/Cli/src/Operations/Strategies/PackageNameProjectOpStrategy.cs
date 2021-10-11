using System;
using System.IO;
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
        private readonly IFileContentCache fileCache;

        public PackageNameProjectOpStrategy(IFileContentCache fileCache)
        {
            this.fileCache = fileCache;
        }

        public async Task<string> OperateAsync(IItem item, CancellationToken cancellationToken = default)
        {
            if (item.Record.Type != ItemType.Project)
            {
                return item.Record.Name;
            }

            string projectFilePath = ProjectStrategyHelper.GetProjectFilePath(item);

            if (!File.Exists(projectFilePath))
            {
                return item.Record.Name;
            }

            var xContent = await fileCache.GetOrLoadXmlContentAsync(projectFilePath, cancellationToken);

            if (xContent.Root is null)
            {
                return Path.GetFileNameWithoutExtension(projectFilePath);
            }

            var @namespace = xContent.Root.GetDefaultNamespace();
            var packageNameElement = xContent.Descendants(@namespace + "PackageId").FirstOrDefault()
                              ?? xContent.Descendants(@namespace + "AssemblyName").FirstOrDefault();

            return packageNameElement?.Value ?? Path.GetFileNameWithoutExtension(projectFilePath);
        }
    }
}
