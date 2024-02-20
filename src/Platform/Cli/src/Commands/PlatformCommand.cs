using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.Platform.Cli.Commands.Account;
using _42.Platform.Cli.Commands.Assistance;
using _42.Platform.Cli.Commands.Storyteller;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands;

[Subcommand(
    typeof(AccountCommand),
    typeof(StorytellerListCommand),
    typeof(SupervisorCommand),
    typeof(SchedulerCommand),
    typeof(AssistCommand))]

[Command(CommandNames.SFORM, Description = "2S platform CLI tooling.")]
public class PlatformCommand : IAsyncCommand
{
    private readonly CommandLineApplication _application;

    public PlatformCommand(CommandLineApplication application)
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
