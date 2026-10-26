using System.IO.Abstractions;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.COMPOSITION, Description = "Show how the effective configuration is composed: the merged documents in order, and the step that set each line.")]
public class ConfigCompositionCommand : BaseContextCommand
{
    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IFileSystem _fileSystem;

    public ConfigCompositionCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi,
        IFileSystem fileSystem)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "An annotation key to show the composition of the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Specify a file where the retrieved composition will be saved.")]
    public string? ExportFilePath { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var annotationKey = Console.ValidateAnnotationKey(AnnotationKey);
        ConfigurationComposition composition;

        try
        {
            composition = await _configurationApi.GetConfigurationCompositionAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                annotationKey,
                if_None_Match: null);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The configuration for '{annotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            // for example a machine restricted to one annotation (AnnotationRestricted): the read needs the whole project
            return Console.WriteApiError(e);
        }

        Console.WriteComposition(composition);

        if (!string.IsNullOrWhiteSpace(ExportFilePath))
        {
            Console.WriteJsonToFile(composition, ExportFilePath, _fileSystem);
        }

        return ExitCodes.SUCCESS;
    }
}
