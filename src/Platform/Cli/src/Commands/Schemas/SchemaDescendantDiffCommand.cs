using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.DIFF, Description = "Show the difference between two versions of a descendant type schema.")]
public class SchemaDescendantDiffCommand : SchemaApiCommand
{
    public SchemaDescendantDiffCommand(
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

    [Argument(2, Description = "The version to compare (to). If not specified, the latest version is used.")]
    public string? ToVersion { get; set; }

    [Argument(3, Description = "The version to compare from. If not specified, the previous version of 'to' is used.")]
    public string? FromVersion { get; set; }

    [Option("--format", CommandOptionType.SingleValue, Description = "Response format passed to the API: 'json' (default) or 'unified'.")]
    public string? Format { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        return SchemaOperations.DiffAsync(
            Console,
            SchemasApi,
            Context,
            SchemaLayer.Descendant,
            annotationType,
            annotationKey,
            ToVersion,
            FromVersion,
            Format);
    }
}
