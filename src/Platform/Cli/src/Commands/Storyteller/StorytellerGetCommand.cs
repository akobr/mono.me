using System.IO.Abstractions;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.GET, Description = "Retrieve a specific annotation.")]
public class StorytellerGetCommand : BaseContextCommand
{
    private readonly IAnnotationsApiAsync _annotationsApi;
    private readonly IFileSystem _fileSystem;

    public StorytellerGetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAnnotationsApiAsync annotationsApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _annotationsApi = annotationsApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation key to get.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Specify a file where the retrieved configuration will be saved.")]
    public string ExportFilePath { get; set; }

    public bool IsExportRequested => !string.IsNullOrWhiteSpace(ExportFilePath);

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(AnnotationKey))
        {
            Console.WriteImportant("Please specify annotation key.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var response = await _annotationsApi.GetAnnotationWithHttpInfoAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            AnnotationKey);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The annotation '{AnnotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteJson(response.Data);

        if (IsExportRequested)
        {
            Console.WriteJsonToFile(response.Data, ExportFilePath, _fileSystem);
        }

        return ExitCodes.SUCCESS;
    }
}
