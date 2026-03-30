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

[Command(CommandNames.GRANT, Description = "Grant access to point to another user.")]
public class AccessPointGrantCommand : BaseCommand
{
    private readonly ApiClient _apiClient;
    private readonly AccessDefaultOptions _accessDefault;

    public AccessPointGrantCommand(
        IExtendedConsole console,
        ApiClient apiClient,
        IOptions<AccessDefaultOptions> accessDefaultOptions)
        : base(console)
    {
        _apiClient = apiClient;
        _accessDefault = accessDefaultOptions.Value;
    }

    [Argument(0, Description = "The target account ID to whom the permissions are granted. The account ID can be retrieved by 'sform account' command.")]
    public string AccountId { get; set; } = string.Empty;

    [Option("-p|--point", CommandOptionType.SingleValue, Description = "A point key to which the access is granted.")]
    public string? ProjectKey { get; set; } = string.Empty;

    [Option("-r|--role", CommandOptionType.SingleValue, Description = "A role which is granted, can be one of the values: Reader, Contributor, Administrator, or Owner.")]
    public string? Role { get; set; } = string.Empty;

    public override async Task<int> OnExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(AccountId))
        {
            Console.WriteImportant("An account ID is required. Ask the person to call 'sform account' command and he will see his account ID.");
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
            role = Permission_Role.Contributor;
        }

        var projectKey = string.IsNullOrWhiteSpace(ProjectKey)
            ? SelectPossiblePoint(account)
            : ProjectKey;

        var point = await _apiClient.V1.Access.Grant.PostAsync(
            new Permission
            {
                CreatedById = account.Id,
                AccountId = AccountId,
                AccessPointKey = projectKey,
                Role = role,
            });

        Console.WriteLine(
            "Access granted to ",
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
            Message = "Which access point would you like to grant access to",
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
