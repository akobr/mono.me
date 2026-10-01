using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.EDIT, Description = "Edit the configuration schema of one annotation. The schema has to exist already.")]
public class SchemaAnnotationEditCommand : SchemaApiCommand
{
    private readonly IEditorService _editorService;
    private readonly EditorOptions _editorOptions;

    public SchemaAnnotationEditCommand(
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

    [Argument(0, Description = "An annotation key to edit the schema for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-f|--force", CommandOptionType.NoValue, Description = "Store the schema even when existing configurations in the view do not comply.")]
    public bool Force { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        return SchemaOperations.EditAsync(
            Console,
            SchemasApi,
            Context,
            _editorService,
            _editorOptions,
            SchemaLayer.Annotation,
            null,
            annotationKey,
            Force);
    }
}
