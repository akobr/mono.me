using System;
using System.Collections.Generic;
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

[Command(CommandNames.GET, Description = "Get an access point with list of users and role. You need to be administrator or owner.")]
public class AccessPointGetCommand : BaseCommand
{
    private readonly ApiClient _apiClient;
    private readonly AccessDefaultOptions _accessDefault;

    public AccessPointGetCommand(
        IExtendedConsole console,
        ApiClient apiClient,
        IOptions<AccessDefaultOptions> accessDefaultOptions)
        : base(console)
    {
        _apiClient = apiClient;
        _accessDefault = accessDefaultOptions.Value;
    }

    [Option("-p|--pointKey", CommandOptionType.SingleValue, Description = "An access point key to get.")]
    public string? PointKey { get; set; }

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

        if (account is null)
        {
            Console.Write(
                "You account is not registered, to create a registration call ",
                "sform account register ".ThemedHighlight(Console.Theme),
                "command.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        var pointKey = PointKey ?? SelectPossiblePoint(account);
        var accessMap = account.AccessMap?.AdditionalData;

        if (accessMap is null || !accessMap.TryGetValue(pointKey, out var roleObject) || roleObject is not string roleString)
        {
            Console.WriteLine($"No access point with key '{pointKey}' has been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (!Enum.TryParse<Permission_Role>(roleString, out var role) || role < Permission_Role.Administrator)
        {
            Console.WriteLine($"You don't have administration permissions to the access point '{pointKey}'.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var point = await _apiClient.V1.Access.Points[pointKey].GetAsync();

        if (point is null)
        {
            Console.WriteLine($"Access point '{pointKey}' has not been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var accesses = point.AccessMap?.AdditionalData.OrderBy(a => a.Key);

        Console.WriteLine();
        Console.WriteHeader("Access point ", pointKey.ThemedHighlight(Console.Theme));

        if (accesses is not null)
        {
            foreach (var access in accesses)
            {
                Console.WriteLine($" - {access.Key} ", $"[{access.Value}]".ThemedLowlight(Console.Theme));
            }
        }

        return ExitCodes.SUCCESS;
    }

    private string SelectPossiblePoint(ApiSdk.Models.Account account)
    {
        var accessMap = account.AccessMap?.AdditionalData ?? new Dictionary<string, object>();
        var selectOptions = new SelectOptions<string>
        {
            Message = "Which access point would you like to get",
            Items = accessMap
                .Where(access => access.Value is string roleString && Enum.TryParse<Permission_Role>(roleString, out var role) && role >= Permission_Role.Administrator)
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
