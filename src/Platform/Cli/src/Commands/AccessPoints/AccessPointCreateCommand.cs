using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Account;
using _42.Platform.Cli.Configuration;
using ApiSdk;
using ApiSdk.Models;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.Platform.Cli.Commands.AccessPoints;

[Command(CommandNames.CREATE, Description = "Create a new project (access point).")]
public class AccessPointCreateCommand : BaseCommand
{
    private readonly ApiClient _apiClient;
    private readonly IFileSystem _fileSystem;

    public AccessPointCreateCommand(
        IExtendedConsole console,
        ApiClient apiClient,
        IFileSystem fileSystem)
        : base(console)
    {
        _apiClient = apiClient;
        _fileSystem = fileSystem;
    }

    public override async Task<int> OnExecuteAsync()
    {
        ApiSdk.Models.Account? account;

        try
        {
            account = await _apiClient.V1.Access.Account.GetAsync();
        }
        catch (ErrorResponse)
        {
            account = null;
        }

        if (account is null)
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

    private string SelectOrInputOrganizationName(ApiSdk.Models.Account account)
    {
        const string createNew = "Create new";
        var accessMap = account.AccessMap?.AdditionalData ?? new Dictionary<string, object>();
        var existingOrganizations = accessMap
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
