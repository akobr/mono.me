using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete the configuration schema of one annotation (its history is kept).")]
public class SchemaAnnotationDeleteCommand : SchemaApiCommand
{
    public SchemaAnnotationDeleteCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi)
        : base(console, context, schemasApi)
    {
    }

    [Argument(0, Description = "An annotation key to delete the schema for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    protected override Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        return SchemaOperations.DeleteAsync(Console, SchemasApi, Context, SchemaLayer.Annotation, null, annotationKey);
    }
}
