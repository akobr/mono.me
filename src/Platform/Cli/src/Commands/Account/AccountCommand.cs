using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands.Account.Settings;
using _42.Platform.Cli.Commands.AccessPoints;
using _42.Platform.Cli.Commands.MachineAccess;
using _42.Platform.Cli.Commands.SharedCertificates;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using _42.Platform.Storyteller.Sdk.Credentials;
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
    typeof(AccountSettingsCommand),
    typeof(AccountLogoutCommand))]

[Command(CommandNames.ACCOUNT, CommandNames.ACCESS, CommandNames.LOGIN, Description = "Manage your account and access to 2S platform services.")]
public class AccountCommand : BaseCommand
{
    private readonly IAccessApiClient _accessApi;
    private readonly IAuthenticationService _authentication;
    private readonly AccessDefaultOptions _accessDefault;
    private readonly IMachineSignIn _machine;
    private readonly ICommandContext _context;
    private readonly SformEnvironment _environment;
    private readonly IBrowserLauncher _browser;

    public AccountCommand(
        IExtendedConsole console,
        IAccessApiClient accessApi,
        IAuthenticationService authentication,
        IOptions<AccessDefaultOptions> accessDefaultOptions,
        IMachineSignIn machine,
        ICommandContext context,
        SformEnvironment environment,
        IBrowserLauncher browser)
        : base(console)
    {
        _accessApi = accessApi;
        _authentication = authentication;
        _accessDefault = accessDefaultOptions.Value;
        _machine = machine;
        _context = context;
        _environment = environment;
        _browser = browser;
    }

    [Option("-b|--browser", CommandOptionType.NoValue, Description = "Open the sign-in page in the browser: the program in SFORM_BROWSER or BROWSER, else the default browser.")]
    public bool OpenBrowser { get; set; }

    [Option("-o|--organization", CommandOptionType.SingleValue, Description = "The id of the organization to sign in to, needed without a terminal when you belong to several.")]
    public string? Organization { get; set; }

    public override async Task<int> OnExecuteAsync()
    {
        if (_machine.IsConfigured)
        {
            return await SignInMachineAsync();
        }

        if (_environment.TokenStoreNotice is { } notice)
        {
            Console.WriteImportant(notice);
        }

        var organization = string.IsNullOrWhiteSpace(Organization) ? null : Organization.Trim();

        try
        {
            var user = await _authentication.GetSignedInUserAsync();

            if (user is null)
            {
                user = await _authentication.LoginWithDeviceCodeAsync(ShowSignInPromptAsync, SelectOrganizationAsync, organization);
                Console.WriteImportant($"You have been logged in as {user.UserName}");
            }
            else if (organization is not null && !string.Equals(user.OrganizationId, organization, StringComparison.OrdinalIgnoreCase))
            {
                // -o is never ignored: a stored session of another organization, or of none (Entra ID), is not the one asked for.
                Console.WriteImportant(
                    user.OrganizationId is null
                        ? $"You are signed in as {user.UserName} without an organization, and -o|--organization asks for '{organization}'."
                        : $"You are signed in as {user.UserName} to the organization '{user.OrganizationId}', and -o|--organization asks for '{organization}'.");
                Console.WriteLine("Sign out with sform account logout, then sign in with -o.".ThemedLowlight(Console.Theme));
                return ExitCodes.ERROR_WRONG_INPUT;
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
        catch (OutputException exception)
        {
            // Several organizations on redirected input: the reason is on standard error already.
            return exception.ExitCode;
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
            MachineSignInKind.TokenFile => "a token file",
            MachineSignInKind.Federated => "workload identity federation",
            _ => kind.ToString(),
        };
    }

    // Non-interactive: no device code, no prompt and no account call, which machines are not allowed. A deprecated
    // variable is named whatever the outcome; other commands stay quiet about it.
    private async Task<int> SignInMachineAsync()
    {
        var exitCode = await SignInAndDescribeMachineAsync();

        foreach (var variable in _machine.Settings.DeprecatedVariables)
        {
            Console.WriteImportant($"{variable} is deprecated; use {StorytellerVariables.Deprecated[variable]}.");
        }

        return exitCode;
    }

    private async Task<int> SignInAndDescribeMachineAsync()
    {
        // A federated machine exchanges at the token endpoint of its project, so the project is checked and bound
        // before any request. Invalid variables fall through to SignInAsync, which reports them.
        if (_machine.TryGetIdentity() is { Kind: MachineSignInKind.Federated } && CheckTarget() is { } targetExitCode)
        {
            return targetExitCode;
        }

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

        if (CheckTarget() is { } exitCode)
        {
            return exitCode;
        }

        Console.WriteLine(
            "Project ",
            $"{_context.OrganizationName}.{_context.ProjectName}".ThemedHighlight(Console.Theme),
            " with ",
            _context.ViewName.ThemedHighlight(Console.Theme),
            " view.");
        Console.WriteLine("API ", (_machine.Settings.Endpoint ?? string.Empty).ThemedHighlight(Console.Theme));

        // The expiry is the exp of a JWT; an opaque token that sform requested has none.
        var expiry = (identity.TokenExpiresAt, identity.Kind) switch
        {
            ({ } expiresAt, _) => $"Token valid until {expiresAt:O}.",
            (null, MachineSignInKind.ClientCredentials or MachineSignInKind.Federated) => "Signed in; the expiry of the token is unknown.",
            _ => "The API checks the credentials on the first call.",
        };
        Console.WriteLine(expiry.ThemedLowlight(Console.Theme));
        return ExitCodes.SUCCESS;
    }

    // Null when the project is known and the target passes its checks, else the exit code.
    private int? CheckTarget()
    {
        if (!_context.IsProjectKnown)
        {
            Console.WriteImportant(
                $"No project is set: set {TargetVariables.PROJECT}=<organization>.<project>, set {StorytellerVariables.KEY} to a full key of the project, or pass -p to each command.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        try
        {
            // The checks every command runs: e.g. STORYTELLER_PROJECT or STORYTELLER_KEY names another project than the
            // credential, or STORYTELLER_KEY is no full key.
            _context.TrySetExplicitTarget(null, null);
            return null;
        }
        catch (AuthenticationException exception)
        {
            Console.WriteImportant(exception.Message);
            return ToExitCode(exception);
        }
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
        if (_environment.IsDeviceCodeJson)
        {
            // The contract for programs, before any other output: one line on standard error, flushed at once.
            var error = Console.Console.Error;
            error.Write(prompt.ToJsonLine());
            error.Write('\n');
            error.Flush();
        }

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

        if (OpenBrowser && !_browser.TryOpen(prompt.VerificationUriComplete ?? prompt.VerificationUri))
        {
            Console.WriteLine("The browser could not be opened, please open the address manually.".ThemedLowlight(Console.Theme));
        }

        return Task.CompletedTask;
    }

    // Asked only without -o, after the code was confirmed, when WorkOS offers several organizations.
    private Task<string> SelectOrganizationAsync(IReadOnlyList<OrganizationChoice> organizations)
    {
        Console.EnsureInteractiveValue(
            "the organization to sign in to",
            [
                "You belong to several organizations; pass one with -o|--organization <id>:",
                .. organizations.Select(choice => $"  {choice.Id}  {choice.Name}"),
            ]);

        var organization = Console.Select(new SelectOptions<OrganizationChoice>
        {
            Message = "You belong to several organizations, which one do you want to sign in to",
            Items = organizations,
            TextSelector = choice => $"{choice.Name} ({choice.Id})",
        });

        return Task.FromResult(organization.Id);
    }

    private int ReportSignInFailure(AuthenticationException exception)
    {
        switch (exception.Reason)
        {
            case AuthenticationFailureReason.Cancelled:
                Console.WriteImportant("The log in operation has been cancelled, please try it again later.");
                return ExitCodes.ERROR_WRONG_INPUT;
            case AuthenticationFailureReason.Expired:
                Console.WriteImportant("The sign-in code expired before the sign-in was completed, please try it again.");
                return ExitCodes.ERROR_WRONG_INPUT;
            case AuthenticationFailureReason.Denied:
                Console.WriteImportant("The sign-in was declined.");
                return ExitCodes.WARNING_UNAUTHORIZED_ACCESS;
            case AuthenticationFailureReason.Configuration:
                // e.g. -o with an organization the user is not a member of: nothing was stored.
                Console.WriteImportant(exception.Message);
                return ExitCodes.ERROR_WRONG_INPUT;
            default:
                Console.WriteImportant("The log in operation failed, please try it again later.");
                Console.WriteLine();
                Console.WriteLine(exception.Message);
                return ExitCodes.ERROR_SERVICE_FAILURE;
        }
    }
}
