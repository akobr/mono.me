using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete the configuration schema of an annotation type (its history is kept).")]
public class SchemaTypeDeleteCommand : SchemaApiCommand
{
    public SchemaTypeDeleteCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi)
        : base(console, context, schemasApi)
    {
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe).")]
    public string AnnotationType { get; set; } = string.Empty;

    protected override Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        return SchemaOperations.DeleteAsync(Console, SchemasApi, Context, SchemaLayer.Type, annotationType, null);
    }
}
