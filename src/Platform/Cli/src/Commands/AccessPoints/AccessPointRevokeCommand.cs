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

namespace _42.Platform.Cli.Commands.AccessPoints;

[Command(CommandNames.REVOKE, Description = "Revoke access to point to another user.")]
public class AccessPointRevokeCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;
    private readonly AccessDefaultOptions _accessDefault;

    public AccessPointRevokeCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi,
        IOptions<AccessDefaultOptions> accessDefaultOptions)
        : base(console)
    {
        _accessApi = accessApi;
        _accessDefault = accessDefaultOptions.Value;
    }

    [Argument(0, Description = "The target account key to whom the permissions are revoked.")]
    public string AccountKey { get; set; } = string.Empty;

    [Option("-p|--point", CommandOptionType.SingleValue, Description = "A point key to which the access is revoked.")]
    public string? ProjectKey { get; set; } = string.Empty;

    [Option("-r|--role", CommandOptionType.SingleValue, Description = "A role which is revoked, can be one of the values: Reader, Contributor, Administrator, or Owner. If not specified all roles will be revoked.")]
    public string? Role { get; set; } = string.Empty;

    public override async Task<int> OnExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(AccountKey))
        {
            Console.WriteImportant("An account key is required.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var accountResponse = await _accessApi.GetAccountWithHttpInfoSafeAsync();

        if (accountResponse.StatusCode is not HttpStatusCode.OK)
        {
            Console.Write(
                "You account is not registered, to create a registration call ",
                "sform account register ".ThemedHighlight(Console.Theme),
                "command.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        var account = accountResponse.Data;
        if (Enum.TryParse<Permission.RoleEnum>(Role, true, out var role))
        {
            role = Permission.RoleEnum.Reader;
        }

        var projectKey = string.IsNullOrWhiteSpace(ProjectKey)
            ? SelectPossiblePoint(account)
            : ProjectKey;

        var point = await _accessApi.RevokeUserAccessAsync(
            new Permission(account.Key, AccountKey, projectKey, role));

        Console.WriteLine(
            "Access revoked to ",
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
        var selectOptions = new SelectOptions<string>
        {
            Message = "Which access point would you like to revoke access to",
            Items = account.AccessMap
                .Where(access => access.Value >= Sdk.Model.Account.InnerEnum.Administrator)
                .Select(access => access.Key)
                .OrderBy(access => access),
        };

        if (!string.IsNullOrWhiteSpace(_accessDefault.ProjectName))
        {
            selectOptions.DefaultValue = $"{_accessDefault.OrganizationName}.{_accessDefault.ProjectName}";
        }

        var projectKey = Console.Select(selectOptions);
        return projectKey;
    }
}
