using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Command(CommandNames.LIST, Description = "Show list of items in a specific location.")]
    public class ListCommand : BaseCommand
    {
        public ListCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Option("-w|--worksteads", CommandOptionType.NoValue, Description = "Display the list of top-level worksteads.")]
        public bool DisplayWorksteads { get; set; }

        [Option("-t|--tree", CommandOptionType.NoValue, Description = "Display the entire hierarchical structure as tree.")]
        public bool DisplayTree { get; set; }

        protected override async Task<int> ExecuteAsync()
        {
            var item = Context.Item;
            var record = item.Record;

            if (DisplayWorksteads || record.Type is RecordType.Repository or RecordType.Special)
            {
                item = Context.Repository;
                await ShowItemHeader(item);

                if (DisplayTree)
                {
                    await ShowTree(item);
                }
                else
                {
                    await ShowListOfItems(Context.Repository.GetWorksteads(), "Workstead");
                }

                return ExitCodes.SUCCESS;
            }

            if (record.Type > RecordType.Workstead)
            {
                item = item.TryGetConcreteItem(RecordType.Workstead);
            }

            if (item is not IWorkstead workstead)
            {
                throw new InvalidOperationException("An unsupported repository position.");
            }

            await ShowItemHeader(item);

            if (DisplayTree)
            {
                await ShowTree(item);
            }
            else
            {
                await ShowListOfItems(workstead.GetSubWorksteads(), "Workstead");
                await ShowListOfItems(workstead.GetProjects(), "Project");
            }

            return ExitCodes.SUCCESS;
        }

        private async Task ShowItemHeader(IItem item)
        {
            var record = item.Record;
            var exactVersions = await item.GetExactVersionsAsync();

            Console.WriteHeader(
                $"{record.GetTypeAsString()}: ",
                record.Name.ThemedHighlight(Console.Theme),
                $" {exactVersions.Version}");

            Console.WriteLine(
                $"Path: {record.Path.GetRelativePath(Context.Repository.Record.Path)}".ThemedLowlight(Console.Theme));
            Console.WriteLine(
                $"Version: {exactVersions.PackageVersion}".ThemedLowlight(Console.Theme));
        }

        private async Task ShowListOfItems(IReadOnlyCollection<IItem> items, string itemType)
        {
            if (items.Count < 1)
            {
                return;
            }

            var tableRows = new List<IEnumerable<string>>();
            foreach (var item in items)
            {
                tableRows.Add(new List<string>
                {
                    $"> {item.Record.Name}",
                    $"{(await item.GetExactVersionsAsync()).PackageVersion}",
                });
            }

            Console.WriteLine();
            Console.WriteHeader($"{itemType}s");
            Console.WriteTable(
                tableRows,
                new[] { itemType, "Version" });
        }

        private async Task ShowTree(IItem item)
        {
            var root = new Composition(string.Empty);
            await BuildTreeAsync(item, root);

            Console.WriteLine();
            Console.WriteTree(root.Children.First(), n => n);
        }

        private async Task BuildTreeAsync(IItem item, Composition parent)
        {
            var node = new Composition($"{item.Record.GetTypeAsChar()} {item.Record.Name} ({(await item.GetExactVersionsAsync()).PackageVersion})");
            parent.Children.Add(node);

            foreach (var child in item.GetChildren())
            {
                await BuildTreeAsync(child, node);
            }
        }
    }
}
