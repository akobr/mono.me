using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;
using ApiSdk;
using ApiSdk.Models;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.REGISTER, Description = "Register new account with 2S platform.")]
public class AccountRegisterCommand : BaseCommand
{
    private static readonly Regex InvalidCharacterRegex = new(@"[^a-z0-9\-_]", RegexOptions.Compiled);

    private readonly ApiClient _apiClient;
    private readonly IAuthenticationService _authentication;
    private readonly IFileSystem _fileSystem;

    public AccountRegisterCommand(
        IExtendedConsole console,
        ApiClient apiClient,
        IAuthenticationService authentication,
        IFileSystem fileSystem)
        : base(console)
    {
        _apiClient = apiClient;
        _authentication = authentication;
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

        if (account is not null)
        {
            Console.WriteImportant(
                $"You are already registered as {account.UserName} ",
                $"#{account.Id}".ThemedLowlight(Console.Theme));
            return ExitCodes.SUCCESS;
        }

        var auth = await _authentication.GetAuthenticationAsync();

        if (auth is null)
        {
            Console.WriteImportant(
                "You are not logged in, please call ",
                "sform login".ThemedHighlight(Console.Theme),
                " first.");

            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        var uniqueName = GetRequiredClaim(auth.ClaimsPrincipal.Claims.ToList(), "unique_name", "upn", "preferred_username");
        var identifier = uniqueName ?? "live#john.doe@outlook.com";

        var organizationName = Console.Input(new InputOptions<string>
        {
            Message = "Name of your organization",
            DefaultValue = GetPossibleOrganizationName(identifier),
        });

        var projectName = Console.Input(new InputOptions<string>
        {
            Message = "Name of your project",
            DefaultValue = "main",
        });

        var projectKey = $"{organizationName}.{projectName}";
        var newAccount = await _apiClient.V1.Access.Account.PostAsync(new AccountCreate { Organization = organizationName, Project = projectName });
        Console.WriteImportant($"Account {newAccount?.UserName ?? "Unknown"} ({newAccount?.Name ?? "Unknown"}) has been created.");
        Console.WriteLine(
            "Default project set to ",
            projectKey.ThemedHighlight(Console.Theme),
            " with ",
            Platform.Storyteller.Constants.DefaultViewName.ThemedHighlight(Console.Theme),
            " view.");

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

    private static string? GetRequiredClaim(IReadOnlyCollection<Claim> claims, params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var uniqueNameClaim = claims.FirstOrDefault(c => c.Type == claimType);
            if (uniqueNameClaim is not null)
            {
                return uniqueNameClaim.Value;
            }
        }

        return null;
    }

    private static string GetPossibleOrganizationName(string text)
    {
        var lowerKey = text.Trim().ToLowerInvariant();
        var normalizedKey = InvalidCharacterRegex.Replace(lowerKey, "_");
        return normalizedKey;
    }
}
