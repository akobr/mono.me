using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.DEFINITION, Description = "Get the combined configuration schema enforced for an annotation.")]
public class SchemaDefinitionCommand : SchemaApiCommand
{
    public SchemaDefinitionCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi)
        : base(console, context, schemasApi)
    {
    }

    [Argument(0, Description = "An annotation key to get the combined schema for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    protected override Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        return SchemaOperations.DefinitionAsync(Console, SchemasApi, Context, annotationKey);
    }
}
