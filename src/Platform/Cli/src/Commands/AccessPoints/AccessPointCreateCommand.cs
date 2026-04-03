using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Account;
using _42.Platform.Cli.Configuration;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.Platform.Cli.Commands.AccessPoints;

[Command(CommandNames.CREATE, Description = "Create a new project (access point).")]
public class AccessPointCreateCommand : BaseCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IFileSystem _fileSystem;

    public AccessPointCreateCommand(
        IExtendedConsole console,
        IAccessApiClient accessApi,
        IFileSystem fileSystem)
        : base(console)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
    }

    public override async Task<int> OnExecuteAsync()
    {
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

        var organizationName = SelectOrInputOrganizationName(account);

        var projectName = Console.Input(new InputOptions<string>
        {
            Message = "Name of your new project",
        });

        await _accessApi.CreateAccessPointAsync(new AccessPointCreate
        {
            Organization = organizationName,
            Project = projectName,
        });

        AccountSetCommand.CreateAccessDefaultConfigFile(
            new AccessDefaultOptions
            {
                OrganizationName = organizationName,
                ProjectName = projectName,
                ViewName = Platform.Storyteller.Constants.DefaultViewName,
            },
            _fileSystem);
        return ExitCodes.SUCCESS;
    }

    private string SelectOrInputOrganizationName(_42.Platform.Storyteller.Sdk.Account account)
    {
        const string createNew = "Create new";
        var existingOrganizations = account.AccessMap
            .Where(access => !access.Key.Contains('.'))
            .Select(access => access.Key)
            .OrderBy(access => access);

        var organizationName = Console.Select(new SelectOptions<string>
        {
            Message = "Select existing organization or create new",
            Items = new[] { createNew }.Concat(existingOrganizations),
            DefaultValue = createNew,
        });

        if (organizationName == createNew)
        {
            organizationName = Console.Input(new InputOptions<string>
            {
                Message = "Name of your organization",
            });
        }

        return organizationName;
    }
}

