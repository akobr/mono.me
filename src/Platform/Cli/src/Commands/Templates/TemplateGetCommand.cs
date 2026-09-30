using System.IO.Abstractions;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Templates;

[Subcommand(
    typeof(TemplateSetCommand),
    typeof(TemplateEditCommand),
    typeof(TemplateDeleteCommand),
    typeof(TemplateVersionsCommand),
    typeof(TemplateDiffCommand))]

[Command(CommandNames.TEMPLATE, CommandNames.TEMPLATES, Description = "Get and manage the configuration template of an annotation type in the view.")]
public class TemplateGetCommand : BaseContextCommand
{
    private readonly ITemplatesApiClient _templatesApi;
    private readonly IFileSystem _fileSystem;

    public TemplateGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        ITemplatesApiClient templatesApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _templatesApi = templatesApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe) to get the template for.")]
    public string AnnotationType { get; set; } = string.Empty;

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Specify a file where the retrieved template will be saved.")]
    public string? ExportFilePath { get; set; }

    [Option("--version", CommandOptionType.SingleValue, Description = "Retrieve a specific version of the template (see 'versions').")]
    public int? Version { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);

        try
        {
            var data = Version.HasValue
                ? await _templatesApi.GetTemplateVersionAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    annotationType,
                    Version.Value)
                : await _templatesApi.GetTemplateAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    annotationType);

            Console.WriteJson(data);

            if (!string.IsNullOrWhiteSpace(ExportFilePath))
            {
                Console.WriteJsonToFile(data, ExportFilePath, _fileSystem);
            }
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine(Version.HasValue
                ? $"The version {Version} of the template for '{annotationType}' has not been found."
                : $"The template for '{annotationType}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        return ExitCodes.SUCCESS;
    }
}
