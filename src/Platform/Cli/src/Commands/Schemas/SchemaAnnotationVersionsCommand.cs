using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.VERSIONS, Description = "List the versions of an annotation schema.")]
public class SchemaAnnotationVersionsCommand : SchemaApiCommand
{
    private readonly IProjectSettingsProvider? _projectSettings;

    public SchemaAnnotationVersionsCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi,
        IProjectSettingsProvider? projectSettings = null)
        : base(console, context, schemasApi)
    {
        _projectSettings = projectSettings;
    }

    [Argument(0, Description = "An annotation key to list the schema versions for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    protected override Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        return SchemaOperations.VersionsAsync(Console, SchemasApi, Context, SchemaLayer.Annotation, null, annotationKey, _projectSettings);
    }
}
