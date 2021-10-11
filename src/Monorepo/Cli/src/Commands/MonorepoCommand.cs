using System.Threading.Tasks;
using _42.Monorepo.Cli.Commands.Init;
using _42.Monorepo.Cli.Commands.New;
using _42.Monorepo.Cli.Commands.Release;
using _42.Monorepo.Cli.Commands.Show;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Monorepo.Cli.Commands
{
    [Subcommand(
        typeof(RunCommand),
        typeof(InitCommand),
        typeof(NewCommand),
        typeof(InfoCommand),
        typeof(ListCommand),
        typeof(ShowCommand),
        typeof(ReleaseCommand),
        typeof(ExplainCommand))]

    [Command(CommandNames.MREPO, Description = "Mono-repository CLI tooling.")]
    public class MonorepoCommand : IAsyncCommand
    {
        private readonly CommandLineApplication application;

        public MonorepoCommand(CommandLineApplication application)
        {
            this.application = application;
        }

        [VersionOption("-v|--version", "", Description = "Display version of this tool.")]
        public bool IsVersionRequested { get; set; }

        public Task<int> OnExecuteAsync()
        {
            application.ShowHelp();
            return Task.FromResult(ExitCodes.SUCCESS);
        }
    }
}
