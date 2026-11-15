using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.Platform.Cli.Commands.Account;
using _42.Platform.Cli.Commands.Assistance;
using _42.Platform.Cli.Commands.Projects;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Services.Projects;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands;

[Subcommand(
    typeof(AccountCommand),
    typeof(ProjectCommand),
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

    // Read by Program once the command line is parsed (OutputSwitches), as every command inherits it.
    [Option("--" + OutputSwitches.BUILTIN_OPTION, CommandOptionType.NoValue, Inherited = true, Description = "Show the built-in type names and dates in the console output, not the project settings.")]
    public bool IsBuiltInRequested { get; set; }

    public Task<int> OnExecuteAsync()
    {
        _application.ShowHelp();
        return Task.FromResult(ExitCodes.SUCCESS);
    }
}
