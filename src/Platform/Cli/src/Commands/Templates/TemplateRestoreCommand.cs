using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Templates;

[Command(CommandNames.RESTORE, Description = "Restore a previous version of the template as its new version (see 'versions').")]
public class TemplateRestoreCommand : BaseContextCommand
{
    private readonly ITemplatesApiClient _templatesApi;

    public TemplateRestoreCommand(
        IExtendedConsole console,
        ICommandContext context,
        ITemplatesApiClient templatesApi)
        : base(console, context)
    {
        _templatesApi = templatesApi;
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe) to restore a version of the template for.")]
    public string AnnotationType { get; set; } = string.Empty;

    // Long only: -v is the view.
    [Option("--version", CommandOptionType.SingleValue, Description = "The version to restore (see 'versions'); its content is stored whole as the new version, also after the template was deleted.")]
    public int? Version { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        var version = Console.ValidateVersionToRestore(Version);

        return Console.RestoreVersionAsync(
            $"the template for '{annotationType}'",
            () => _templatesApi.RestoreTemplateVersionAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                annotationType,
                version));
    }
}
