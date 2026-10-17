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
    private readonly IMachineSignIn _machine;

    public AccountLogoutCommand(
        IExtendedConsole console,
        IAccessApiClient accessApi,
        IAuthenticationService authentication,
        IMachineSignIn machine)
        : base(console)
    {
        _accessApi = accessApi;
        _authentication = authentication;
        _machine = machine;
    }

    public override async Task<int> OnExecuteAsync()
    {
        if (_machine.IsConfigured)
        {
            // The stored user session is left alone: it is not the identity in use.
            Console.WriteImportant(
                $"sform uses the machine credentials in {string.Join(", ", _machine.Settings.CredentialVariables)}. Nothing is stored, so there is nothing to log out; unset the variables to sign in as a user.");
            return ExitCodes.SUCCESS;
        }

        var user = await _authentication.GetSignedInUserAsync();

        if (user is null)
        {
            // Still clears an expired session and the discovered provider.
            await _authentication.LogoutAsync();
            Console.WriteImportant("No account is logged in this CLI instance.");
            return ExitCodes.SUCCESS;
        }

        try
        {
            var account = await _accessApi.GetAccountAsync();
            await _authentication.LogoutAsync();
            Console.WriteImportant(
                "You have been logged out from account: ",
                account.Name.ThemedHighlight(Console.Theme));
        }
        catch (ApiException e) when (e.StatusCode is (int)HttpStatusCode.NotFound)
        {
            await _authentication.LogoutAsync();
            Console.WriteImportant("You have been logged out.");
        }

        return ExitCodes.SUCCESS;
    }
}

