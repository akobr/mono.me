using System;
using System.Threading.Tasks;
using _42.Monorepo.Cli.Output;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands.Update
{
    [Command(CommandNames.PACKAGE, Description = "Update version of a specific package.")]
    public class UpdatePackageCommand : BaseCommand
    {
        public UpdatePackageCommand(IExtendedConsole console, ICommandContext context)
            : base(console, context)
        {
            // no operation
        }

        [Argument(0, Description = "Id of the package.")]
        public string? Package { get; set; } = string.Empty;

        protected override Task<int> ExecuteAsync()
        {
            throw new NotImplementedException();
        }
    }
}
