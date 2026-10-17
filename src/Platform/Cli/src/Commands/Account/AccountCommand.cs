using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands.AccessPoints;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Cli.Commands.SharedCertificates;
using _42.Platform.Cli.Configuration;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Account;

[Subcommand(
    typeof(AccountRegisterCommand),
    typeof(AccountSetCommand),
    typeof(AccessPointListCommand),
    typeof(MachineListCommand),
    typeof(SharedCertListCommand),
    typeof(CaDownloadCommand),
    typeof(AccountLogoutCommand))]

[Command(CommandNames.ACCOUNT, CommandNames.ACCESS, CommandNames.LOGIN, Description = "Manage your account and access to 2S platform services.")]
public class AccountCommand : BaseCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IAuthenticationService _authentication;
    private readonly AccessDefaultOptions _accessDefault;
    private readonly IMachineSignIn _machine;
    private readonly ICommandContext _context;

    public AccountCommand(
        IExtendedConsole console,
        IAccessApiClient accessApi,
        IAuthenticationService authentication,
        IOptions<AccessDefaultOptions> accessDefaultOptions,
        IMachineSignIn machine,
        ICommandContext context)
        : base(console)
    {
        _accessApi = accessApi;
        _authentication = authentication;
        _accessDefault = accessDefaultOptions.Value;
        _machine = machine;
        _context = context;
    }

    [Option("-b|--browser", CommandOptionType.NoValue, Description = "Open the sign-in page in the default browser.")]
    public bool OpenBrowser { get; set; }

    public override async Task<int> OnExecuteAsync()
    {
        if (_machine.IsConfigured)
        {
            return await SignInMachineAsync();
        }

        try
        {
            var user = await _authentication.GetSignedInUserAsync();

            if (user is null)
            {
                user = await _authentication.LoginWithDeviceCodeAsync(ShowSignInPromptAsync, SelectOrganizationAsync);
                Console.WriteImportant($"You have been logged in as {user.UserName}");
            }
            else
            {
                Console.WriteImportant($"You are already logged in as {user.UserName}");
                Console.WriteLine();
            }
        }
        catch (AuthenticationException exception)
        {
            return ReportSignInFailure(exception);
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

        Console.WriteHeader($"{account.Name} @ {account.UserName}");
        Console.WriteLine("Account ID: ", $"#{account.Id}".ThemedLowlight(Console.Theme));
        Console.WriteLine($"You have access to {account.AccessMap.Count} access points.");

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
            " with ",
            (_accessDefault.ViewName ?? Platform.Storyteller.Constants.DefaultViewName).ThemedHighlight(Console.Theme),
            " view.");
        Console.WriteLine("You can change it by command sform account set.".ThemedLowlight(Console.Theme));
        return ExitCodes.SUCCESS;
    }

    private static string DescribeKind(MachineSignInKind kind)
    {
        return kind switch
        {
            MachineSignInKind.ApiKey => "an API key",
            MachineSignInKind.Certificate => "a certificate",
            MachineSignInKind.CertificateAndApiKey => "a certificate and an API key",
            MachineSignInKind.ClientCredentials => "client credentials",
            _ => "a token file",
        };
    }

    // Non-interactive: no device code, no prompt and no account call, which machines are not allowed.
    private async Task<int> SignInMachineAsync()
    {
        MachineIdentity identity;

        try
        {
            identity = await _machine.SignInAsync();
        }
        catch (AuthenticationException exception)
        {
            Console.WriteImportant(exception.Message);
            return ToExitCode(exception);
        }

        Console.WriteHeader("Machine sign-in");
        Console.WriteLine(
            "Signed in as ",
            (identity.MachineId is null ? "a machine" : $"machine {identity.MachineId}").ThemedHighlight(Console.Theme),
            $" with {DescribeKind(identity.Kind)} ({string.Join(", ", identity.Variables)}).");

        if (!_context.IsProjectKnown)
        {
            Console.WriteImportant($"No project is set: set {StorytellerVariables.PROJECT}=<organization>.<project> or pass -p to each command.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        try
        {
            // The checks every command runs: e.g. STORYTELLER_PROJECT names another project than the key.
            _context.TrySetExplicitTarget(null, null);
        }
        catch (AuthenticationException exception)
        {
            Console.WriteImportant(exception.Message);
            return ToExitCode(exception);
        }

        Console.WriteLine(
            "Project ",
            $"{_context.OrganizationName}.{_context.ProjectName}".ThemedHighlight(Console.Theme),
            " with ",
            _context.ViewName.ThemedHighlight(Console.Theme),
            " view.");
        Console.WriteLine("API ", (_machine.Settings.ApiUrl ?? string.Empty).ThemedHighlight(Console.Theme));
        Console.WriteLine(identity.TokenExpiresAt is { } expiresAt
            ? $"Token valid until {expiresAt:O}.".ThemedLowlight(Console.Theme)
            : "The API checks the credentials on the first call.".ThemedLowlight(Console.Theme));
        return ExitCodes.SUCCESS;
    }

    private static int ToExitCode(AuthenticationException exception)
    {
        return exception.Reason switch
        {
            AuthenticationFailureReason.Configuration => ExitCodes.ERROR_WRONG_INPUT,
            AuthenticationFailureReason.ServiceError => ExitCodes.ERROR_SERVICE_FAILURE,
            _ => ExitCodes.WARNING_UNAUTHORIZED_ACCESS,
        };
    }

    private Task ShowSignInPromptAsync(DeviceCodePrompt prompt)
    {
        Console.WriteHeader("Sign in");
        Console.WriteLine(
            "To sign in, open ",
            prompt.VerificationUri.AbsoluteUri.ThemedHighlight(Console.Theme),
            " and enter the code ",
            prompt.UserCode.ThemedHighlight(Console.Theme),
            ".");

        if (prompt.VerificationUriComplete is not null)
        {
            Console.WriteLine("Or open ", prompt.VerificationUriComplete.AbsoluteUri.ThemedHighlight(Console.Theme), " with the code filled in.");
        }

        Console.WriteLine($"The code expires in {Math.Max(1, (int)Math.Round(prompt.ExpiresIn.TotalMinutes))} minute(s).".ThemedLowlight(Console.Theme));

        if (OpenBrowser)
        {
            OpenInBrowser(prompt.VerificationUriComplete ?? prompt.VerificationUri);
        }

        return Task.CompletedTask;
    }

    private Task<string> SelectOrganizationAsync(IReadOnlyList<OrganizationChoice> organizations)
    {
        var organization = Console.Select(new SelectOptions<OrganizationChoice>
        {
            Message = "You belong to several organizations, which one do you want to sign in to",
            Items = organizations,
            TextSelector = choice => $"{choice.Name} ({choice.Id})",
        });

        return Task.FromResult(organization.Id);
    }

    private void OpenInBrowser(Uri uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            Console.WriteLine("The browser could not be opened, please open the address manually.".ThemedLowlight(Console.Theme));
        }
    }

    private int ReportSignInFailure(AuthenticationException exception)
    {
        switch (exception.Reason)
        {
            case AuthenticationFailureReason.Cancelled:
                Console.WriteImportant("The log in operation has been cancelled, please try it again later.");
                break;
            case AuthenticationFailureReason.Expired:
                Console.WriteImportant("The sign-in code expired before the sign-in was completed, please try it again.");
                break;
            case AuthenticationFailureReason.Denied:
                Console.WriteImportant("The sign-in was declined.");
                break;
            default:
                Console.WriteImportant("The log in operation failed, please try it again later.");
                Console.WriteLine();
                Console.WriteLine(exception.Message);
                break;
        }

        return ExitCodes.ERROR_WRONG_INPUT;
    }
}
