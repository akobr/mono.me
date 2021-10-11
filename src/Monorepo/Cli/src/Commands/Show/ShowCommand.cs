using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Show
{
    [Command(CommandNames.SHOW, Description = "Show detailed information about a requested subject.")]
    [Subcommand(typeof(ShowUsagesCommand), typeof(ShowDependencyTreeCommand))]
    public class ShowCommand : BaseParentCommand
    {
        public ShowCommand(IExtendedConsole console, ICommandContext context, CommandLineApplication application)
            : base(console, context, application)
        {
            // no operation
        }
    }
}
