using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.VERSIONS, Description = "List the versions of an annotation type schema.")]
public class SchemaTypeVersionsCommand : SchemaApiCommand
{
    private readonly IProjectSettingsProvider? _projectSettings;

    public SchemaTypeVersionsCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi,
        IProjectSettingsProvider? projectSettings = null)
        : base(console, context, schemasApi)
    {
        _projectSettings = projectSettings;
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe).")]
    public string AnnotationType { get; set; } = string.Empty;

    protected override Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        return SchemaOperations.VersionsAsync(Console, SchemasApi, Context, SchemaLayer.Type, annotationType, null, _projectSettings);
    }
}
