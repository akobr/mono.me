using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.LIST, Description = "Get a list of all accessible access points.")]
public class AccountListCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;

    public AccountListCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi)
        : base(console)
    {
        _accessApi = accessApi;
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
        var accessPoints = account.AccessMap;

        foreach (var accessPoint in accessPoints)
        {
            Console.WriteLine($"- {accessPoint.Key} ({accessPoint.Value:G})");
        }

        return ExitCodes.SUCCESS;
    }
}
