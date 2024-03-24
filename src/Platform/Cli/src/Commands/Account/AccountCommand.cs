using System;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands.AccessPoints;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Cli.Configuration;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace _42.Platform.Cli.Commands.Account;

[Subcommand(
    typeof(AccountRegisterCommand),
    typeof(AccountSetCommand),
    typeof(AccessPointListCommand),
    typeof(MachineListCommand))]

[Command(CommandNames.ACCOUNT, CommandNames.ACCESS, CommandNames.LOGIN, Description = "Manage your account and access to 2S platform services.")]
public class AccountCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;
    private readonly IAuthenticationService _authentication;
    private readonly AccessDefaultOptions _accessDefault;

    public AccountCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi,
        IAuthenticationService authentication,
        IOptions<AccessDefaultOptions> accessDefaultOptions)
        : base(console)
    {
        _accessApi = accessApi;
        _authentication = authentication;
        _accessDefault = accessDefaultOptions.Value;
    }

    public override async Task<int> OnExecuteAsync()
    {
        try
        {
            var auth = await _authentication.GetAuthenticationAsync();
            Console.WriteImportant($"You are already logged in as {auth?.Account.Username}");
        }
        catch (MsalUiRequiredException)
        {
            var deviceAuthResultCode = await AcquireByDeviceCodeAsync();

            if (deviceAuthResultCode != ExitCodes.SUCCESS)
            {
                return deviceAuthResultCode;
            }
        }

        var accountResponse = await _accessApi.GetAccountWithHttpInfoSafeAsync();

        if (accountResponse.StatusCode is HttpStatusCode.NotFound)
        {
            Console.Write(
                "You account is not registered, to create a registration call ",
                "sform account register ".ThemedHighlight(Console.Theme),
                "command.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        var account = accountResponse.Data;
        Console.WriteLine("Account ", $"#{account.Key}".ThemedLowlight(Console.Theme));
        Console.WriteLine($"Have access to {account.AccessMap.Count} access points.");

        if (string.IsNullOrWhiteSpace(_accessDefault?.ProjectName))
        {
            Console.WriteImportant(
                "No default project is set, please call ",
                "sform account set".ThemedHighlight(Console.Theme),
                " command.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        var projectKey = $"{_accessDefault.OrganizationName}.{_accessDefault.ProjectName}";
        Console.WriteLine(
            "Default project set to ",
            projectKey.ThemedHighlight(Console.Theme),
            " with view ",
            (_accessDefault.ViewName ?? Platform.Storyteller.Constants.DefaultViewName).ThemedHighlight(Console.Theme),
            ".");
        return ExitCodes.SUCCESS;
    }

    private async Task<int> AcquireByDeviceCodeAsync()
    {
        try
        {
            var pca = await _authentication.GetPublicClientApplicationAsync();
            var result = await pca.AcquireTokenWithDeviceCode(
                _authentication.Scopes,
                deviceCodeResult =>
                {
                    // This will print the message on the console which tells the user where to go sign-in using
                    // a separate browser and the code to enter once they sign in.
                    // The AcquireTokenWithDeviceCode() method will poll the server after firing this
                    // device code callback to look for the successful login of the user via that browser.
                    // This background polling (whose interval and timeout data is also provided as fields in the
                    // deviceCodeCallback class) will occur until:
                    // * The user has successfully logged in via browser and entered the proper code
                    // * The timeout specified by the server for the lifetime of this code (typically ~15 minutes) has been reached
                    // * The developing application calls the Cancel() method on a CancellationToken sent into the method.
                    //   If this occurs, an OperationCanceledException will be thrown (see catch below for more details).
                    Console.WriteHeader("Sign in");
                    Console.WriteLine(deviceCodeResult.Message);
                    return Task.FromResult(ExitCodes.WARNING_INTERACTION_NEEDED);
                }).ExecuteAsync();

            Console.WriteImportant($"You have been logged in as {result.Account.Username}");
            return ExitCodes.SUCCESS;
        }
        // TODO: handle or throw all these exceptions
        catch (MsalServiceException ex)
        {
            // Kind of errors you could have (in ex.Message)

            // AADSTS50059: No tenant-identifying information found in either the request or implied by any provided credentials.
            // Mitigation: as explained in the message from Azure AD, the authoriy needs to be tenanted. you have probably created
            // your public client application with the following authorities:
            // https://login.microsoftonline.com/common or https://login.microsoftonline.com/organizations

            // AADSTS90133: Device Code flow is not supported under /common or /consumers endpoint.
            // Mitigation: as explained in the message from Azure AD, the authority needs to be tenanted

            // AADSTS90002: Tenant <tenantId or domain you used in the authority> not found. This may happen if there are
            // no active subscriptions for the tenant. Check with your subscription administrator.
            // Mitigation: if you have an active subscription for the tenant this might be that you have a typo in the
            // tenantId (GUID) or tenant domain name.
            Console.WriteImportant("The log in operation failed, please try it again later.");
            Console.WriteLine();
            Console.WriteLine(ex.Message);
        }
        catch (OperationCanceledException ex)
        {
            // If you use a CancellationToken, and call the Cancel() method on it, then this *may* be triggered
            // to indicate that the operation was cancelled.
            // See /dotnet/standard/threading/cancellation-in-managed-threads
            // for more detailed information on how C# supports cancellation in managed threads.
            Console.WriteImportant("The log in operation has been cancelled, please try it again later.");
        }
        catch (MsalClientException ex)
        {
            // Possible cause - verification code expired before contacting the server
            // This exception will occur if the user does not manage to sign-in before a time out (15 mins) and the
            // call to `AcquireTokenWithDeviceCode` is not cancelled in between
            Console.WriteImportant("The log in timeout, please try it again later.");
        }

        return ExitCodes.ERROR_WRONG_INPUT;
    }
}
