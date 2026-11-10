using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Schemas;

[Command(CommandNames.RESTORE, Description = "Restore a previous version of an annotation type schema as its new version (see 'versions').")]
public class SchemaTypeRestoreCommand : SchemaApiCommand
{
    public SchemaTypeRestoreCommand(
        IExtendedConsole console,
        ICommandContext context,
        ISchemasApiClient schemasApi)
        : base(console, context, schemasApi)
    {
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe).")]
    public string AnnotationType { get; set; } = string.Empty;

    // Long only: -v is the view.
    [Option("--version", CommandOptionType.SingleValue, Description = "The version to restore (see 'versions'); its content is stored as the new version, also after the schema was deleted.")]
    public int? Version { get; set; }

    [Option("-f|--force", CommandOptionType.NoValue, Description = "Restore the version even when existing configurations in the view do not comply.")]
    public bool Force { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        var version = Console.ValidateVersionToRestore(Version);
        return SchemaOperations.RestoreAsync(Console, SchemasApi, Context, SchemaLayer.Type, annotationType, null, version, Force);
    }
}
