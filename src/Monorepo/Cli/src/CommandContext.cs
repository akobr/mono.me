using System.IO.Abstractions;
using _42.Monorepo.Cli.Commands;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;

namespace _42.Monorepo.Cli
{
    public class CommandContext : ICommandContext
    {
        private readonly IFileSystem _fileSystem;
        private readonly IItemsFactory _powerItemFactory;
        private readonly IItemOptionsProvider _optionsProvider;

        public CommandContext(
            IFileSystem fileSystem,
            IItemsFactory powerItemFactory,
            IItemOptionsProvider optionsProvider)
        {
            _fileSystem = fileSystem;
            _powerItemFactory = powerItemFactory;
            _optionsProvider = optionsProvider;
            var repositoryItem = MonorepoDirectoryFunctions.GetMonoRepository();
            Repository = powerItemFactory.BuildItem<IRepository>(repositoryItem);

            var item = MonorepoDirectoryFunctions.GetCurrentRecord(optionsProvider);
            Item = powerItemFactory.BuildItem(item);
        }

        public bool IsValid => Repository.Record.IsValid;

        public IItem Item { get; set; }

        public IRepository Repository { get; }

        public void ReInitialize(string repoRelativePath)
        {
            var possibleItemPath = _fileSystem.Path.Combine(Repository.Record.Path, repoRelativePath);

            if (!_fileSystem.Directory.Exists(possibleItemPath) && !_fileSystem.File.Exists(possibleItemPath))
            {
                return;
            }

            possibleItemPath = _fileSystem.Path.GetFullPath(possibleItemPath);

            if (!possibleItemPath.StartsWith(Repository.Record.Path, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var item = MonorepoDirectoryFunctions.GetRecord(possibleItemPath, _optionsProvider);
            Item = _powerItemFactory.BuildItem(item);
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
