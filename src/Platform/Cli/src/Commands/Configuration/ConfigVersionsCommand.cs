using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.VERSIONS, Description = "List the versions of a configuration.")]
public class ConfigVersionsCommand : BaseContextCommand
{
    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IProjectSettingsProvider _projectSettings;

    public ConfigVersionsCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi,
        IProjectSettingsProvider? projectSettings = null)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _projectSettings = projectSettings ?? BuiltInProjectSettings.Instance;
    }

    [Argument(0, Description = "An annotation key to list the configuration versions for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var versions = await _configurationApi.GetConfigurationVersionsAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            AnnotationKey);

        Console.WriteHeader($"Versions of the configuration for '{AnnotationKey}'");

        if (versions.Count < 1)
        {
            Console.WriteLine("The configuration has no versions.".ThemedLowlight(Console.Theme));
            return ExitCodes.SUCCESS;
        }

        Console.WriteVersions(versions, await _projectSettings.GetDisplayAsync());
        return ExitCodes.SUCCESS;
    }
}
