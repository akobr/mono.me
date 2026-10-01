using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.EDIT, Description = "Edit the configuration schema of a descendant type under an ancestor. The schema has to exist already.")]
public class SchemaDescendantEditCommand : SchemaApiCommand
{
    private readonly IEditorService _editorService;
    private readonly EditorOptions _editorOptions;

    public SchemaDescendantEditCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi,
        IEditorService editorService,
        IOptions<EditorOptions> editorOptions)
        : base(console, context, schemasApi)
    {
        _editorService = editorService;
        _editorOptions = editorOptions.Value;
    }

    [Argument(0, Description = "The ancestor annotation key.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Argument(1, Description = "The descendant annotation type code (rst, unt, sbt, usg, cnt, exe, uxe).")]
    public string AnnotationType { get; set; } = string.Empty;

    [Option("-f|--force", CommandOptionType.NoValue, Description = "Store the schema even when existing configurations in the view do not comply.")]
    public bool Force { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        return SchemaOperations.EditAsync(
            Console,
            SchemasApi,
            Context,
            _editorService,
            _editorOptions,
            SchemaLayer.Descendant,
            annotationType,
            annotationKey,
            Force);
    }
}
