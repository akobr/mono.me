using _42.Monorepo.Cli.Commands;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli
{
    public class CommandContext : ICommandContext
    {
        public CommandContext(IItemsFactory powerItemFactory)
        {
            var repositoryItem = MonorepoDirectoryFunctions.GetMonoRepository();
            Repository = powerItemFactory.BuildItem<IRepository>(repositoryItem);

            var item = MonorepoDirectoryFunctions.GetCurrentItem();
            Item = powerItemFactory.BuildItem(item);
        }

        public bool IsValid => Repository.Record.IsValid;

        public IItem Item { get; }

        public IRepository Repository { get; }

        public void TryFailedIfInvalid()
        {
            if (!IsValid)
            {
                throw new OutsideMonorepoException("This tool can be used only inside a mono-repository.");
            }
        }
    }
}
