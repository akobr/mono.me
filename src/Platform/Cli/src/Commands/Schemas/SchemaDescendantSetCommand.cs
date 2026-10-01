using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create or replace the configuration schema of a descendant type under an ancestor.")]
public class SchemaDescendantSetCommand : SchemaApiCommand
{
    private readonly IFileSystem _fileSystem;

    public SchemaDescendantSetCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi,
        IFileSystem fileSystem)
        : base(console, context, schemasApi)
    {
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "The ancestor annotation key.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Argument(1, Description = "The descendant annotation type code (rst, unt, sbt, usg, cnt, exe, uxe).")]
    public string AnnotationType { get; set; } = string.Empty;

    [Option("-i|--import", CommandOptionType.SingleValue, Description = "A JSON Schema file. The file replaces the stored schema.")]
    public string? ImportFilePath { get; set; }

    [Option("-f|--force", CommandOptionType.NoValue, Description = "Store the schema even when existing configurations in the view do not comply.")]
    public bool Force { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        var annotationType = Console.ValidateAnnotationType(AnnotationType);

        if (string.IsNullOrWhiteSpace(ImportFilePath))
        {
            Console.WriteLine("Specify a schema file with -i|--import.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var content = await JsonInputBuilder.BuildAsync(Console, _fileSystem, ImportFilePath, null);
        return await SchemaOperations.SetAsync(
            Console,
            SchemasApi,
            Context,
            SchemaLayer.Descendant,
            annotationType,
            annotationKey,
            content,
            Force);
    }
}
