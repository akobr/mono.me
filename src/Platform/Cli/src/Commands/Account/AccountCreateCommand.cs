using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.CREATE, Description = "Create a new project.")]
public class AccountCreateCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;
    private readonly IFileSystem _fileSystem;

    public AccountCreateCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi,
        IFileSystem fileSystem)
        : base(console)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
    }

    public override async Task<int> OnExecuteAsync()
    {
        var accountResponse = await _accessApi.GetAccountWithHttpInfoAsync();

        if (accountResponse.StatusCode is not HttpStatusCode.OK)
        {
            Console.Write(
                "You account is not registered, to create a registration call ",
                "sform account register ".ThemedHighlight(Console.Theme),
                "command.");
            return ExitCodes.INTERACTION_NEEDED;
        }

        var account = accountResponse.Data;
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

    private string SelectOrInputOrganizationName(Sdk.Model.Account account)
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
