using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Templates;

[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create or update a template (merged into the current content, or replacing it with --replace).")]
public class TemplateSetCommand : BaseContextCommand
{
    private readonly ITemplatesApiClient _templatesApi;
    private readonly IFileSystem _fileSystem;

    public TemplateSetCommand(
        IExtendedConsole console,
        ICommandContext context,
        ITemplatesApiClient templatesApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _templatesApi = templatesApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation type code (rst, unt, sbt, usg, cnt, exe, uxe) to set the template for.")]
    public string AnnotationType { get; set; } = string.Empty;

    [Option("-i|--import", CommandOptionType.SingleValue, Description = "Specify a file from where the template will be imported.")]
    public string? ImportFilePath { get; set; }

    [Option("-x|--properties", CommandOptionType.MultipleValue, Description = "Specify inline properties to be set on the template.")]
    public string[]? InlineProperties { get; set; }

    [Option("--replace", CommandOptionType.NoValue, Description = "Replace the content of the template instead of merging into it (removes properties which are not specified).")]
    public bool IsReplaceRequested { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var annotationType = Console.ValidateAnnotationType(AnnotationType);
        var content = await JsonInputBuilder.BuildAsync(Console, _fileSystem, ImportFilePath, InlineProperties);
        ConfigurationTemplate? data;

        try
        {
            if (IsReplaceRequested)
            {
                var currentContent = await _templatesApi.GetTemplateContentAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    annotationType);

                data = await _templatesApi.ReplaceTemplateAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    annotationType,
                    currentContent,
                    content);

                if (data is null)
                {
                    Console.WriteLine("No changes detected.");
                    return ExitCodes.WARNING_NO_WORK_NEEDED;
                }
            }
            else
            {
                data = await _templatesApi.SetTemplateAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    annotationType,
                    content);
            }
        }
        catch (ApiException e)
        {
            Console.WriteLine($"Error occurred: {e.Message}");
            return ExitCodes.ERROR_CRASH;
        }

        Console.WriteJson(data);
        return ExitCodes.SUCCESS;
    }
}
