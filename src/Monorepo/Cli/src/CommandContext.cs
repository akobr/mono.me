using System.IO;
using _42.Monorepo.Cli.Commands;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli
{
    public class CommandContext : ICommandContext
    {
        private readonly IItemsFactory powerItemFactory;

        public CommandContext(IItemsFactory powerItemFactory)
        {
            this.powerItemFactory = powerItemFactory;
            var repositoryItem = MonorepoDirectoryFunctions.GetMonoRepository();
            Repository = powerItemFactory.BuildItem<IRepository>(repositoryItem);

            var item = MonorepoDirectoryFunctions.GetCurrentRecord();
            Item = powerItemFactory.BuildItem(item);
        }

        public bool IsValid => Repository.Record.IsValid;

        public IItem Item { get; set; }

        public IRepository Repository { get; }

        public void ReInitialize(string repoRelativePath)
        {
            var possibleItemPath = Path.Combine(Repository.Record.Path, repoRelativePath);

            if (!Directory.Exists(possibleItemPath) && !File.Exists(possibleItemPath))
            {
                return;
            }

            possibleItemPath = Path.GetFullPath(possibleItemPath);

            if (!possibleItemPath.StartsWith(Repository.Record.Path, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var item = MonorepoDirectoryFunctions.GetRecord(possibleItemPath);
            Item = powerItemFactory.BuildItem(item);
        }

        public void TryFailedIfInvalid()
        {
            if (!IsValid)
            {
                throw new OutsideMonorepoException("This tool can be used only inside a mono-repository.");
            }
        }
    }
}
