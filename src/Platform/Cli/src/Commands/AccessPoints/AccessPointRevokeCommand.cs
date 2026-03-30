using System;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using ApiSdk;
using ApiSdk.Models;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Sharprompt;

namespace _42.Platform.Cli.Commands.AccessPoints;

[Command(CommandNames.REVOKE, Description = "Revoke access to point to another user.")]
public class AccessPointRevokeCommand : BaseCommand
{
    private readonly ApiClient _apiClient;
    private readonly AccessDefaultOptions _accessDefault;

    public AccessPointRevokeCommand(
        IExtendedConsole console,
        ApiClient apiClient,
        IOptions<AccessDefaultOptions> accessDefaultOptions)
        : base(console)
    {
        _apiClient = apiClient;
        _accessDefault = accessDefaultOptions.Value;
    }

    [Argument(0, Description = "The target account key to whom the permissions are revoked.")]
    public string AccountId { get; set; } = string.Empty;

    [Option("-p|--point", CommandOptionType.SingleValue, Description = "A point key to which the access is revoked.")]
    public string? ProjectKey { get; set; } = string.Empty;

    [Option("-r|--role", CommandOptionType.SingleValue, Description = "A role which is revoked, can be one of the values: Reader, Contributor, Administrator, or Owner. If not specified all roles will be revoked.")]
    public string? Role { get; set; } = string.Empty;

    public override async Task<int> OnExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(AccountId))
        {
            Console.WriteImportant("An account ID is required.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (AccountId.StartsWith('#'))
        {
            AccountId = AccountId[1..];
        }

        ApiSdk.Models.Account? account;

        try
        {
            account = await _apiClient.V1.Access.Account.GetAsync();
        }
        catch (ErrorResponse)
        {
            account = null;
        }

        if (account is null)
        {
            Console.Write(
                "You account is not registered, to create a registration call ",
                "sform account register ".ThemedHighlight(Console.Theme),
                "command.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        if (!Enum.TryParse<Permission_Role>(Role, true, out var role))
        {
            role = Permission_Role.Reader;
        }

        var projectKey = string.IsNullOrWhiteSpace(ProjectKey)
            ? SelectPossiblePoint(account)
            : ProjectKey;

        var point = await _apiClient.V1.Access.Revoke.PostAsync(
            new Permission
            {
                CreatedById = account.Id,
                AccountId = AccountId,
                AccessPointKey = projectKey,
                Role = role,
            });

        Console.WriteLine(
            "Access revoked to ",
            AccountId.ThemedHighlight(Console.Theme),
            " with role ",
            $"{role:G}".ThemedHighlight(Console.Theme),
            " to ",
            (projectKey ?? "Unknown").ThemedHighlight(Console.Theme),
            ".");

        return ExitCodes.SUCCESS;
    }

    private string SelectPossiblePoint(ApiSdk.Models.Account account)
    {
        var selectOptions = new SelectOptions<string>
        {
            Message = "Which access point would you like to revoke access to",
            Items = account.AccessMap?.AdditionalData
                .Select(access => access.Key)
                .OrderBy(access => access) ?? Enumerable.Empty<string>(),
        };

        if (!string.IsNullOrWhiteSpace(_accessDefault.ProjectName))
        {
            selectOptions.DefaultValue = $"{_accessDefault.OrganizationName}.{_accessDefault.ProjectName}";
        }

        var projectKey = Console.Select(selectOptions);
        return projectKey;
    }
}
