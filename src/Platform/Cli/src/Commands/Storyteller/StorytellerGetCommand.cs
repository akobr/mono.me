using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using ApiSdk;
using ApiSdk.Models;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.GET, Description = "Retrieve a specific annotation.")]
public class StorytellerGetCommand : BaseContextCommand
{
    private readonly ApiClient _apiClient;
    private readonly IFileSystem _fileSystem;

    public StorytellerGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        ApiClient apiClient,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _apiClient = apiClient;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation key to get.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Specify a file where the retrieved configuration will be saved.")]
    public string ExportFilePath { get; set; } = string.Empty;

    public bool IsExportRequested => !string.IsNullOrWhiteSpace(ExportFilePath);

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(AnnotationKey))
        {
            Console.WriteImportant("Please specify annotation key.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Annotation? annotation;

        try
        {
            annotation = await _apiClient.V1[Context.OrganizationName][Context.ProjectName][Context.ViewName].Annotations[AnnotationKey]
                .GetAsync();
        }
        catch (ErrorResponse)
        {
            annotation = null;
        }

        if (annotation is null)
        {
            Console.WriteLine($"The annotation '{AnnotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteJson(annotation);

        if (IsExportRequested)
        {
            Console.WriteJsonToFile(annotation, ExportFilePath, _fileSystem);
        }

        return ExitCodes.SUCCESS;
    }
}
