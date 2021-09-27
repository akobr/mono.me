using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;
using Semver;

namespace _42.Monorepo.Cli.Commands
{
    [Command("list", Description = "List top level worksteads inside the mono-repository.")]
    public class ListCommand : BaseCommand
    {
        public ListCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
        }

        protected override async Task ExecuteAsync()
        {
            var worksteadTable = new List<IEnumerable<string>>();
            foreach (var workstead in Context.Repository.GetWorksteads())
            {
                worksteadTable.Add(new List<string>
                {
                    $" > {workstead.Record.Name} ",
                    $"{await workstead.TryGetDefinedVersionAsync() ?? new SemVersion(1)}",
                });
            }

            Console.WriteTable(
                worksteadTable,
                new[] { "Workstead", "Version" });
        }
    }
}
