using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.GET, Description = "Get the configuration schema of one annotation in the view.")]
public class SchemaAnnotationGetCommand : SchemaApiCommand
{
    public SchemaAnnotationGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi)
        : base(console, context, schemasApi)
    {
    }

    [Argument(0, Description = "An annotation key to get the schema for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("--version", CommandOptionType.SingleValue, Description = "Retrieve a specific version of the schema (see 'versions').")]
    public int? Version { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        return SchemaOperations.GetAsync(Console, SchemasApi, Context, SchemaLayer.Annotation, null, annotationKey, Version);
    }
}
