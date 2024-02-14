using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Sdk.Api;
using _42.Platform.Sdk.Model;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Sharprompt;

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.GRANT, Description = "Grant access to your project to another user.")]
public class AccountGrantCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;
    private readonly AccessDefaultOptions _accessDefault;

    public AccountGrantCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi,
        IOptions<AccessDefaultOptions> accessDefaultOptions)
        : base(console)
    {
        _accessApi = accessApi;
        _accessDefault = accessDefaultOptions.Value;
    }

    [Argument(0, Description = "The target account key to whom the permissions are granted.")]
    public string AccountKey { get; } = string.Empty;

    [Option("-p|--point", CommandOptionType.SingleValue, Description = "A point key to which the access is granted.")]
    public string? ProjectKey { get; } = string.Empty;

    [Option("-r|--role", CommandOptionType.SingleValue, Description = "A role which is granted.")]
    public string? Role { get; } = string.Empty;

    public override async Task<int> OnExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(AccountKey))
        {
            Console.WriteImportant("An account key is required.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

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
        if (Enum.TryParse<Permission.RoleEnum>(Role, true, out var role))
        {
            role = Permission.RoleEnum.Contributor;
        }

        var projectKey = string.IsNullOrWhiteSpace(ProjectKey)
            ? SelectPossiblePoint(account)
            : ProjectKey;

        var point = await _accessApi.GrantUserAccessAsync(
            new Permission(account.Key, AccountKey, projectKey, role));

        Console.WriteLine(
            "Access granted to ",
            AccountKey.ThemedHighlight(Console.Theme),
            " with role ",
            $"{role:G}".ThemedHighlight(Console.Theme),
            " to ",
            projectKey.ThemedHighlight(Console.Theme),
            ".");

        return ExitCodes.SUCCESS;
    }

    private string SelectPossiblePoint(Sdk.Model.Account account)
    {
        var projectKey = Console.Select(new SelectOptions<string>
        {
            Message = "Which point to grant access to",
            DefaultValue = _accessDefault.ProjectKey,
            Items = account.AccessMap
                .Where(access => access.Value >= Sdk.Model.Account.InnerEnum.Administrator)
                .Select(access => access.Key)
                .OrderBy(access => access),
        });
        return projectKey;
    }
}