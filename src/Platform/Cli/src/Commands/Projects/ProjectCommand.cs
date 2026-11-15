using System.Threading.Tasks;
using _42.CLI.Toolkit;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Projects;

[Subcommand(typeof(ProjectSettingsCommand))]

[Command(CommandNames.PROJECT, Description = "Get and manage the project of the context: its settings.")]
public class ProjectCommand : IAsyncCommand
{
    private readonly CommandLineApplication _application;

    public ProjectCommand(CommandLineApplication application)
    {
        _application = application;
    }

    public Task<int> OnExecuteAsync()
    {
        _application.ShowHelp();
        return Task.FromResult(ExitCodes.SUCCESS);
    }
}
