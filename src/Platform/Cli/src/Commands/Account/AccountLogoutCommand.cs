using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.LOGOUT, Description = "Logout out from this CLI instance.")]
public class AccountLogoutCommand : BaseCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IAuthenticationService _authentication;

    public AccountLogoutCommand(
        IExtendedConsole console,
        IAccessApiClient accessApi,
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

        try
        {
            var account = await _accessApi.GetAccountAsync();
            await _authentication.ClearAuthenticationAsync();
            Console.WriteImportant(
                "You have been logged out from account: ",
                account.Name.ThemedHighlight(Console.Theme));
        }
        catch (ApiException e) when (e.StatusCode is (int)HttpStatusCode.NotFound)
        {
            await _authentication.ClearAuthenticationAsync();
            Console.WriteImportant("You have been logged out.");
        }

        return ExitCodes.SUCCESS;
    }
}

