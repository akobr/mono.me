using System.Threading.Tasks;
using _42.CLI.Toolkit;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Subcommand(
    typeof(SchemaDescendantGetCommand),
    typeof(SchemaDescendantSetCommand),
    typeof(SchemaDescendantEditCommand),
    typeof(SchemaDescendantDeleteCommand),
    typeof(SchemaDescendantVersionsCommand),
    typeof(SchemaDescendantDiffCommand))]

[Command(CommandNames.DESCENDANT, Description = "Get and manage the configuration schema of a descendant type under an ancestor in the view.")]
public class SchemaDescendantCommand : IAsyncCommand
{
    private readonly CommandLineApplication _application;

    public SchemaDescendantCommand(CommandLineApplication application)
    {
        _application = application;
    }

    public Task<int> OnExecuteAsync()
    {
        _application.ShowHelp();
        return Task.FromResult(ExitCodes.SUCCESS);
    }
}
