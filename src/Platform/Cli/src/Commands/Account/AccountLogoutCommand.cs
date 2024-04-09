using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.LOGOUT, Description = "Logout out from this CLI instance.")]
public class AccountLogoutCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;
    private readonly IAuthenticationService _authentication;

    public AccountLogoutCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi,
        IAuthenticationService authentication)
        : base(console)
    {
        _accessApi = accessApi;
        _authentication = authentication;
    }

    public override async Task<int> OnExecuteAsync()
    {
        var auth = await _authentication.GetAuthenticationAsync();

        if (auth is null)
        {
            Console.WriteImportant("No account is logged in this CLI instance.");
            return ExitCodes.SUCCESS;
        }

        var accountResponse = await _accessApi.GetAccountWithHttpInfoAsync();
        await _authentication.ClearAuthenticationAsync();

        if (accountResponse.StatusCode is HttpStatusCode.OK)
        {
            Console.WriteImportant(
                "You have been logged out from account: ",
                accountResponse.Data.Name.ThemedHighlight(Console.Theme));
        }
        else
        {
            Console.WriteImportant("You have been logged out.");
        }

        return ExitCodes.SUCCESS;
    }
}
