using _42.CLI.Toolkit.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Update
{
    [Command(CommandNames.UPDATE, "u", Description = "Update version or package in a current location.")]
    [Subcommand(typeof(UpdatePackageCommand), typeof(UpdateVersionCommand))]
    public class UpdateCommand : BaseParentCommand
    {
        public UpdateCommand(IExtendedConsole console, ICommandContext context, CommandLineApplication application)
            : base(console, context, application)
        {
            // no operation
        }
    }
}
