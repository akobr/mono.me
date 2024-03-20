using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.AccessPoints;

[Subcommand(
    typeof(AccessPointGetCommand),
    typeof(AccessPointCreateCommand),
    typeof(AccessPointGrantCommand),
    typeof(AccessPointRevokeCommand))]

[Command(CommandNames.POINTS, Description = "Get and manage access points.")]
public class AccessPointListCommand : BaseCommand
{
    private readonly IAccessApiAsync _accessApi;

    public AccessPointListCommand(
        IExtendedConsole console,
        IAccessApiAsync accessApi)
        : base(console)
    {
        _accessApi = accessApi;
    }

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
        var accessPoints = account.AccessMap;

        Console.WriteHeader("Access points");

        foreach (var accessPoint in accessPoints)
        {
            Console.WriteLine($"- {accessPoint.Key} ", $"[{accessPoint.Value:G}]".ThemedLowlight(Console.Theme));
        }

        return ExitCodes.SUCCESS;
    }
}
