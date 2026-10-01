using System.Threading.Tasks;
using _42.CLI.Toolkit;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Subcommand(
    typeof(SchemaTypeGetCommand),
    typeof(SchemaTypeSetCommand),
    typeof(SchemaTypeEditCommand),
    typeof(SchemaTypeDeleteCommand),
    typeof(SchemaTypeVersionsCommand),
    typeof(SchemaTypeDiffCommand))]

[Command(CommandNames.TYPE, Description = "Get and manage the configuration schema of an annotation type in the view.")]
public class SchemaTypeCommand : IAsyncCommand
{
    private readonly CommandLineApplication _application;

    public SchemaTypeCommand(CommandLineApplication application)
    {
        _application = application;
    }

    public Task<int> OnExecuteAsync()
    {
        _application.ShowHelp();
        return Task.FromResult(ExitCodes.SUCCESS);
    }
}
