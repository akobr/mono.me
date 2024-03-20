using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Configuration;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Options;
using Sharprompt;

namespace _42.Platform.Cli.Commands.AccessPoints;

[Command(CommandNames.GET, Description = "Get an access point with list of users and role. You need to be administrator or owner.")]
public class AccessPointGetCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;
    private readonly AccessDefaultOptions _accessDefault;

    public AccessPointGetCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi,
        IOptions<AccessDefaultOptions> accessDefaultOptions)
        : base(console)
    {
        _accessApi = accessApi;
        _accessDefault = accessDefaultOptions.Value;
    }

    [Option("-p|--pointKey", CommandOptionType.SingleValue, Description = "An access point key to get.")]
    public string? PointKey { get; set; }

    public override async Task<int> OnExecuteAsync()
    {
        var accountResponse = await _accessApi.GetAccountWithHttpInfoSafeAsync();

        if (accountResponse.StatusCode is not HttpStatusCode.OK)
        {
            Console.Write(
                "You account is not registered, to create a registration call ",
                "sform account register ".ThemedHighlight(Console.Theme),
                "command.");
            return ExitCodes.INTERACTION_NEEDED;
        }

        var account = accountResponse.Data;
        var pointKey = PointKey ?? SelectPossiblePoint(account);

        if (!account.AccessMap.TryGetValue(pointKey, out var role))
        {
            Console.WriteLine($"No access point with key '{pointKey}' has been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (role < Sdk.Model.Account.InnerEnum.Administrator)
        {
            Console.WriteLine($"You don't have administration permissions to the access point '{pointKey}'.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var point = await _accessApi.GetAccessPointAsync(pointKey);
        var accesses = point.AccessMap.OrderBy(a => a.Key);

        Console.WriteLine();
        Console.WriteHeader("Access point ", pointKey.ThemedHighlight(Console.Theme));

        foreach (var access in accesses)
        {
            Console.WriteLine($" - {access.Key} ", $"[{access.Value:G}]".ThemedLowlight(Console.Theme));
        }

        return ExitCodes.SUCCESS;
    }

    private string SelectPossiblePoint(Sdk.Model.Account account)
    {
        var selectOptions = new SelectOptions<string>
        {
            Message = "Which access point would you like to get",
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
