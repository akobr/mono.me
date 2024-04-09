using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Configuration;
using _42.Platform.Sdk.Api;
using _42.Platform.Sdk.Model;
using _42.Platform.Storyteller.Access;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.REGISTER, Description = "Register new account with 2S platform.")]
public class AccountRegisterCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;
    private readonly IAuthenticationService _authentication;
    private readonly IFileSystem _fileSystem;

    public AccountRegisterCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi,
        IAuthenticationService authentication,
        IFileSystem fileSystem)
        : base(console)
    {
        _accessApi = accessApi;
        _authentication = authentication;
        _fileSystem = fileSystem;
    }

    public override async Task<int> OnExecuteAsync()
    {
        var accountResponse = await _accessApi.GetAccountWithHttpInfoAsync();

        if (accountResponse.StatusCode is HttpStatusCode.OK)
        {
            Console.WriteImportant(
                $"You are already registered as {accountResponse.Data.UserName} ",
                $"#{accountResponse.Data.Id}".ThemedLowlight(Console.Theme));
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
            DefaultValue = identifier.ToNormalizedKey(),
        });

        var projectName = Console.Input(new InputOptions<string>
        {
            Message = "Name of your project",
            DefaultValue = "main",
        });

        var projectKey = $"{organizationName}.{projectName}";
        var newAccount = await _accessApi.CreateAccountAsync(new AccountCreate(organization: organizationName, project: projectName));
        Console.WriteImportant($"Account {newAccount.UserName} ({newAccount.Name}) has been created.");
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
}
