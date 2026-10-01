using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.GET, Description = "Get the configuration schema of a descendant type under an ancestor in the view.")]
public class SchemaDescendantGetCommand : SchemaApiCommand
{
    public SchemaDescendantGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi)
        : base(console, context, schemasApi)
    {
    }

    [Argument(0, Description = "The ancestor annotation key.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Argument(1, Description = "The descendant annotation type code (rst, unt, sbt, usg, cnt, exe, uxe).")]
    public string AnnotationType { get; set; } = string.Empty;

    [Option("--version", CommandOptionType.SingleValue, Description = "Retrieve a specific version of the schema (see 'versions').")]
    public int? Version { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        return SchemaOperations.GetAsync(
            Console,
            SchemasApi,
            Context,
            SchemaLayer.Descendant,
            annotationType,
            annotationKey,
            Version);
    }
}
