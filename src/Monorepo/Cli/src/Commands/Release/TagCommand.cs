using System;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Release
{
    [Command(CommandNames.TAG, Description = "Create new git tag after a release.")]
    public class TagCommand : BaseCommand
    {
        public TagCommand(IExtendedConsole console, ICommandContext context)
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
