using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Model;
using _42.Monorepo.Cli.Model.Items;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Command(CommandNames.LIST, Description = "List items in current position of the mono-repository.")]
    public class ListCommand : BaseCommand
    {
        public ListCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Option("-w|--worksteads", CommandOptionType.NoValue, Description = "Display the list of top-level worksteads.")]
        public bool DisplayWorksteads { get; set; }

        protected override async Task<int> ExecuteAsync()
        {
            var item = Context.Item;
            var record = item.Record;

            if (DisplayWorksteads || record.Type == RecordType.Repository)
            {
                item = Context.Repository;
                await ShowItemHeader(item);
                await ShowListOfItems(Context.Repository.GetWorksteads(), "Workstead");
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
            await ShowListOfItems(workstead.GetSubWorksteads(), "Workstead");
            await ShowListOfItems(workstead.GetProjects(), "Project");
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
    }
}
