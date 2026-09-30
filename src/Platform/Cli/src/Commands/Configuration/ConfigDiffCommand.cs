using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.DIFF, Description = "Show difference between two configuration versions.")]
public class ConfigDiffCommand : BaseContextCommand
{
    private readonly IConfigurationsApiClient _configurationApi;

    /// <summary>
    /// Initializes a new instance of ConfigDiffCommand with the required console, command context, and configuration API client.
    /// </summary>
    public ConfigDiffCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi)
        : base(console, context)
    {
        _configurationApi = configurationApi;
    }

    [Argument(0, Description = "An annotation key to get the configuration diff for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    [Argument(1, Description = "The version to compare (to). If not specified, the latest version is used.")]
    public string? ToVersion { get; set; }

    [Argument(2, Description = "The version to compare from. If not specified, the previous version of 'to' is used.")]
    public string? FromVersion { get; set; }

    [Option("--view-to-compare", CommandOptionType.SingleValue, Description = "The view to compare with. Current view is used as from and the specified view as to.")]
    public string? TargetView { get; set; }

    /// <summary>
    /// Runs the diff command: validates arguments, retrieves the requested configuration diff, and writes a formatted, colorized diff to the console.
    /// </summary>
    protected override async Task<int> ExecuteAsync()
    {
        try
        {
            // Validation: mutual exclusion check
            if (!string.IsNullOrWhiteSpace(TargetView) &&
                (!string.IsNullOrWhiteSpace(ToVersion) || !string.IsNullOrWhiteSpace(FromVersion)))
            {
                Console.WriteLine("Cannot specify both --view-to-compare and version arguments (ToVersion or FromVersion).");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            // Validation: integer parsing check
            if (!string.IsNullOrWhiteSpace(ToVersion) && !int.TryParse(ToVersion, out _))
            {
                Console.WriteLine($"ToVersion '{ToVersion}' is not a valid integer.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            if (!string.IsNullOrWhiteSpace(FromVersion) && !int.TryParse(FromVersion, out _))
            {
                Console.WriteLine($"FromVersion '{FromVersion}' is not a valid integer.");
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            DiffResult diff;

            if (!string.IsNullOrWhiteSpace(TargetView))
            {
                diff = await _configurationApi.GetConfigurationViewDiffAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey,
                    TargetView,
                    null);
            }
            else if (int.TryParse(ToVersion, out var toVersion))
            {
                if (int.TryParse(FromVersion, out var fromVersion))
                {
                    diff = await _configurationApi.GetConfigurationVersionDiffCustomAsync(
                        Context.OrganizationName,
                        Context.ProjectName,
                        Context.ViewName,
                        AnnotationKey,
                        toVersion,
                        fromVersion,
                        null);
                }
                else
                {
                    diff = await _configurationApi.GetConfigurationVersionDiffAsync(
                        Context.OrganizationName,
                        Context.ProjectName,
                        Context.ViewName,
                        AnnotationKey,
                        toVersion,
                        null);
                }
            }
            else
            {
                // Default: compare latest version with its previous
                var versions = await _configurationApi.GetConfigurationVersionsAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey);

                if (versions.Count == 0)
                {
                    Console.WriteLine($"The configuration for '{AnnotationKey}' has no versions.");
                    return ExitCodes.ERROR_WRONG_INPUT;
                }

                var latestVersion = versions.OrderByDescending(v => v.Version).First().Version;
                diff = await _configurationApi.GetConfigurationVersionDiffAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    AnnotationKey,
                    latestVersion,
                    null);
            }

            Console.WriteDiffResult(diff);
        }
        catch (ApiException e) when (e.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine($"The configuration for '{AnnotationKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        return ExitCodes.SUCCESS;
    }
}
