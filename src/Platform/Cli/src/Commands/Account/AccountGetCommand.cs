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

namespace _42.Platform.Cli.Commands.Account;

[Command(CommandNames.GET, Description = "Get an access point with list of user accesses. You need to be admin or owner.")]
public class AccountGetCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;
    private readonly AccessDefaultOptions _accessDefault;

    public AccountGetCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi,
        IOptions<AccessDefaultOptions> accessDefaultOptions)
        : base(console)
    {
        _accessApi = accessApi;
        _accessDefault = accessDefaultOptions.Value;
    }

    [Argument(0, Description = "An access point key to get.")]
    public string? ProjectKey { get; } = string.Empty;

    public override async Task<int> OnExecuteAsync()
    {
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
        var pointKey = ProjectKey ?? SelectPossibleProject(account);

        if (account.AccessMap.TryGetValue(pointKey, out var role))
        {
            Console.WriteLine($"No access point with key '{pointKey}' has been found.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (role < Sdk.Model.Account.InnerEnum.Administrator)
        {
            Console.WriteLine($"You don't have administrator permissions to the access point '{pointKey}'.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var point = await _accessApi.GetAccessPointAsync(pointKey);
        var accesses = point.AccessMap.OrderBy(a => a.Key);
        Console.WriteLine("Access point: ", pointKey.ThemedHighlight(Console.Theme));

        foreach (var access in accesses)
        {
            Console.WriteLine($" - {access.Key} ({access.Value:G})");
        }

        return ExitCodes.SUCCESS;
    }

    private string SelectPossibleProject(Sdk.Model.Account account)
    {
        var projectKey = Console.Select(new SelectOptions<string>
        {
            Message = "Which access point would you like to get",
            DefaultValue = _accessDefault.ProjectKey,
            Items = account.AccessMap
                .Select(access => access.Key)
                .OrderBy(access => access),
        });
        return projectKey;
    }
}
