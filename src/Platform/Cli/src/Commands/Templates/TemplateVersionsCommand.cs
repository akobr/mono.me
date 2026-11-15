using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Templates;

[Command(CommandNames.VERSIONS, Description = "List the versions of a template.")]
public class TemplateVersionsCommand : BaseContextCommand
{
    private readonly ITemplatesApiClient _templatesApi;
    private readonly IProjectSettingsProvider _projectSettings;

    public TemplateVersionsCommand(
        IExtendedConsole console,
        ICommandContext context,
        ITemplatesApiClient templatesApi,
        IProjectSettingsProvider? projectSettings = null)
        : base(console, context)
    {
        _templatesApi = templatesApi;
        _projectSettings = projectSettings ?? BuiltInProjectSettings.Instance;
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe) to list the template versions for.")]
    public string AnnotationType { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        var versions = await _templatesApi.GetTemplateVersionsAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            annotationType);

        Console.WriteHeader($"Versions of the template for '{annotationType}'");

        if (versions.Count < 1)
        {
            Console.WriteLine("The template has no versions.".ThemedLowlight(Console.Theme));
            return ExitCodes.SUCCESS;
        }

        Console.WriteVersions(versions, await _projectSettings.GetDisplayAsync());
        return ExitCodes.SUCCESS;
    }
}
