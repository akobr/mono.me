using System;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Update
{
    [Command(CommandNames.VERSION, Description = "Update version of a current location based on git history.")]
    public class UpdateVersionCommand : BaseCommand
    {
        public UpdateVersionCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        protected override Task<int> ExecuteAsync()
        {
            throw new NotImplementedException();
        }
    }
}
