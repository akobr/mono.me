using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.RESTORE, Description = "Restore a previous version of the configuration as its new version (see 'versions').")]
public class ConfigRestoreCommand : BaseContextCommand
{
    private readonly IConfigurationsApiClient _configurationApi;

    public ConfigRestoreCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi)
        : base(console, context)
    {
        _configurationApi = configurationApi;
    }

    [Argument(0, Description = "An annotation key to restore a version of the configuration for.")]
    public string AnnotationKey { get; set; } = string.Empty;

    // Long only: -v is the view.
    [Option("--version", CommandOptionType.SingleValue, Description = "The version to restore (see 'versions'); its content is stored whole as the new version, also after the configuration was deleted.")]
    public int? Version { get; set; }

    [Option("-f|--force", CommandOptionType.NoValue, Description = "Restore the version even when it violates the schema.")]
    public bool Force { get; set; }

    protected override Task<int> ExecuteAsync()
    {
        string key = Console.ValidateAnnotationKey(AnnotationKey);
        var version = Console.ValidateVersionToRestore(Version);

        return Console.RestoreVersionAsync(
            $"the configuration for '{key}'",
            () => _configurationApi.RestoreConfigurationVersionAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                key,
                version,
                Force));
    }
}
