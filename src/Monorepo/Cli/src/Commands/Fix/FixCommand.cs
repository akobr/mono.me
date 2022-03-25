using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Fix
{
    [Command(CommandNames.FIX, Description = "Fix problem in a current location.")]
    [Subcommand(typeof(FixPackagesCommand))]
    public class FixCommand : BaseParentCommand
    {
        public FixCommand(IExtendedConsole console, ICommandContext context, CommandLineApplication application)
            : base(console, context, application)
        {
            // no operation
        }
    }
}
