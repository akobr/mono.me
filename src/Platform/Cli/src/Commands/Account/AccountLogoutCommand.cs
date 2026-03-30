using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using ApiSdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.LOGOUT, Description = "Logout out from this CLI instance.")]
public class AccountLogoutCommand : BaseCommand
{
    private readonly ApiClient _apiClient;
    private readonly IAuthenticationService _authentication;

    public AccountLogoutCommand(
        IExtendedConsole console,
        ApiClient apiClient,
        IAuthenticationService authentication)
        : base(console)
    {
        _apiClient = apiClient;
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

        ApiSdk.Models.Account? account = null;

        try
        {
            account = await _apiClient.V1.Access.Account.GetAsync();
        }
        catch (ApiSdk.Models.ErrorResponse)
        {
            // ignore
        }

        await _authentication.ClearAuthenticationAsync();

        if (account is not null)
        {
            Console.WriteImportant(
                "You have been logged out from account: ",
                (account.Name ?? "Unknown").ThemedHighlight(Console.Theme));
        }
        else
        {
            Console.WriteImportant("You have been logged out.");
        }

        return ExitCodes.SUCCESS;
    }
}
