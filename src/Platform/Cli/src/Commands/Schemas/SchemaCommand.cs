using System.Threading.Tasks;
using _42.CLI.Toolkit;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Subcommand(
    typeof(SchemaTypeCommand),
    typeof(SchemaAnnotationCommand),
    typeof(SchemaDescendantCommand),
    typeof(SchemaDefinitionCommand))]

[Command(CommandNames.SCHEMA, Description = "Get and manage configuration schemas in the view.")]
public class SchemaCommand : IAsyncCommand
{
    private readonly CommandLineApplication _application;

    public SchemaCommand(CommandLineApplication application)
    {
        _application = application;
    }

    public Task<int> OnExecuteAsync()
    {
        _application.ShowHelp();
        return Task.FromResult(ExitCodes.SUCCESS);
    }
}
