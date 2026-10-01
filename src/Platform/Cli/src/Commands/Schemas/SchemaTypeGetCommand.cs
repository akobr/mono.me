using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.GET, Description = "Get the configuration schema of an annotation type in the view.")]
public class SchemaTypeGetCommand : SchemaApiCommand
{
    public SchemaTypeGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi)
        : base(console, context, schemasApi)
    {
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe).")]
    public string AnnotationType { get; set; } = string.Empty;

    [Option("--version", CommandOptionType.SingleValue, Description = "Retrieve a specific version of the schema (see 'versions').")]
    public int? Version { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        return SchemaOperations.GetAsync(Console, SchemasApi, Context, SchemaLayer.Type, annotationType, null, Version);
    }
}
