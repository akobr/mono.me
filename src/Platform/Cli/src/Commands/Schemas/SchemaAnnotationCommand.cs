using System.Threading.Tasks;
using _42.CLI.Toolkit;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Subcommand(
    typeof(SchemaAnnotationGetCommand),
    typeof(SchemaAnnotationSetCommand),
    typeof(SchemaAnnotationEditCommand),
    typeof(SchemaAnnotationDeleteCommand),
    typeof(SchemaAnnotationVersionsCommand),
    typeof(SchemaAnnotationDiffCommand))]

[Command(CommandNames.ANNOTATION, Description = "Get and manage the configuration schema of one annotation in the view.")]
public class SchemaAnnotationCommand : IAsyncCommand
{
    private readonly CommandLineApplication _application;

    public SchemaAnnotationCommand(CommandLineApplication application)
    {
        _application = application;
    }

    public Task<int> OnExecuteAsync()
    {
        _application.ShowHelp();
        return Task.FromResult(ExitCodes.SUCCESS);
    }
}
