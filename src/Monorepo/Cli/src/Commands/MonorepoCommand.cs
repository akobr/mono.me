using System.Threading.Tasks;
using _42.Monorepo.Cli.Commands.Fix;
using _42.Monorepo.Cli.Commands.New;
using _42.Monorepo.Cli.Commands.Release;
using _42.Monorepo.Cli.Commands.Show;
using _42.Monorepo.Cli.Commands.Update;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Subcommand(
        typeof(BuildCommand),
        typeof(RunCommand),
        typeof(NewCommand),
        typeof(InfoCommand),
        typeof(ListCommand),
        typeof(ShowCommand),
        typeof(UpdateCommand),
        typeof(FixCommand),
        typeof(ReleaseCommand),
        typeof(ExplainCommand))]

    [Command(CommandNames.MREPO, Description = "Mono-repository CLI tooling.")]
    public class MonorepoCommand : IAsyncCommand
    {
        private readonly CommandLineApplication _application;

        public MonorepoCommand(CommandLineApplication application)
        {
            _application = application;
        }

        [VersionOption("-v|--version", "", Description = "Display version of this tool.")]
        public bool IsVersionRequested { get; set; }

        public Task<int> OnExecuteAsync()
        {
            _application.ShowHelp();
            return Task.FromResult(ExitCodes.SUCCESS);
        }
    }
}
