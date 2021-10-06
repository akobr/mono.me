using System.Threading.Tasks;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Show
{
    [Command("show", Description = "Show detailed information about a requested subject.")]
    [Subcommand(typeof(ShowUsagesCommand), typeof(ShowDependencyTreeCommand))]
    public class ShowCommand : BaseCommand
    {
        public ShowCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        protected override Task ExecuteAsync()
        {
            return Task.CompletedTask;
        }
    }
}
