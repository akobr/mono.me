using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.SET, Description = "Set default project and view of the CLI toolkit.")]
public class AccountSetCommand : BaseCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IUserSettingsStore _settings;
    private readonly AccessDefaultOptions _accessDefault;

    public AccountSetCommand(
        IExtendedConsole console,
        IAccessApiClient accessApi,
        IOptions<AccessDefaultOptions> accessDefaultOptions,
        IUserSettingsStore settings)
        : base(console)
    {
        _accessApi = accessApi;
        _settings = settings;
        _accessDefault = accessDefaultOptions.Value;
    }

    [Option("-p|--projectKey", CommandOptionType.SingleValue, Description = "A project key to set as default.")]
    public string? ProjectKey { get; set; }

    [Option("-v|--viewName", CommandOptionType.SingleValue, Description = "A view name to set as default.")]
    public string? ViewName { get; set; }

    public override async Task<int> OnExecuteAsync()
    {
        if (ProjectKey is not null && !UserSettingDefinitions.TrySplitProjectKey(ProjectKey, out _, out _))
        {
            Console.WriteImportant($"'{ProjectKey}' is no project key; it takes <organization>.<project>.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        _42.Platform.Storyteller.Sdk.Account account;

        try
        {
            account = await _accessApi.GetAccountAsync();
        }
        catch (ApiException e) when (e.StatusCode is (int)HttpStatusCode.NotFound)
        {
            Console.Write(
                "You account is not registered, to create a registration call ",
                "sform account register ".ThemedHighlight(Console.Theme),
                "command.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        if (!string.IsNullOrWhiteSpace(_accessDefault.ProjectName))
        {
            var currentProjectKey = $"{_accessDefault.OrganizationName}.{_accessDefault.ProjectName}";
            Console.WriteLine(
                "Currently the default project is set to ",
                currentProjectKey.ThemedHighlight(Console.Theme),
                " and view is ",
                (_accessDefault.ViewName ?? Platform.Storyteller.Constants.DefaultViewName).ThemedHighlight(Console.Theme),
                ".");
        }

        var projectKey = (ProjectKey ?? SelectPossibleProject(account)).Trim();
        UserSettingDefinitions.TrySplitProjectKey(projectKey, out var organizationName, out var projectName);
        _accessDefault.ProjectName = projectName;
        _accessDefault.OrganizationName = organizationName;
        _accessDefault.ViewName = ViewName ?? _accessDefault.ViewName ?? Platform.Storyteller.Constants.DefaultViewName;

        try
        {
            _settings.Update(
                ConfigurationSections.ACCESS,
                UserSettingsSchema.AccessChanges(_accessDefault.OrganizationName, _accessDefault.ProjectName, _accessDefault.ViewName));
        }
        catch (UserSettingsException exception)
        {
            Console.WriteImportant($"The default project could not be saved: {exception.Message}");
            return exception.ExitCode;
        }

        Console.WriteLine(
            "Default project set to ",
            projectKey.ThemedHighlight(Console.Theme),
            " with view ",
            _accessDefault.ViewName.ThemedHighlight(Console.Theme),
            ".");
        return ExitCodes.SUCCESS;
    }

    // After the main work of register and access-point create, which a failure to save does not undo.
    internal static void SaveDefaultProject(IExtendedConsole console, IUserSettingsStore settings, string organizationName, string projectName)
    {
        try
        {
            settings.Update(
                ConfigurationSections.ACCESS,
                UserSettingsSchema.AccessChanges(organizationName, projectName, Platform.Storyteller.Constants.DefaultViewName));
        }
        catch (UserSettingsException exception)
        {
            console.WriteImportant($"The default project could not be saved: {exception.Message}");
        }
    }

    private string SelectPossibleProject(_42.Platform.Storyteller.Sdk.Account account)
    {
        var selectOptions = new SelectOptions<string>
        {
            Message = "Which project would you like to set as default",
            Items = account.AccessMap
                .Where(access => access.Key.Contains('.'))
                .Select(access => access.Key)
                .OrderBy(access => access),
        };

        if (!string.IsNullOrWhiteSpace(_accessDefault.ProjectName))
        {
            selectOptions.DefaultValue = $"{_accessDefault.OrganizationName}.{_accessDefault.ProjectName}";
        }

        var projectKey = Console.Select(selectOptions);
        return projectKey;
    }
}

