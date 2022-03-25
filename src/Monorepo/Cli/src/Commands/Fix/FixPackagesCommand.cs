using System;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Fix
{
    [Command(CommandNames.PACKAGES, Description = "Move all locally versioned packages to centralized point.")]
    public class FixPackagesCommand : BaseCommand
    {
        public FixPackagesCommand(IExtendedConsole console, ICommandContext context)
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
