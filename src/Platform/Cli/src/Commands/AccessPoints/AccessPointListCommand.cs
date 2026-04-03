using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
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
    private readonly IAccessApiClient _accessApi;

    public AccessPointListCommand(
        IExtendedConsole console,
        IAccessApiClient accessApi)
        : base(console)
    {
        _accessApi = accessApi;
    }

    public override async Task<int> OnExecuteAsync()
    {
        _42.Platform.Storyteller.Sdk.Account account;

        try
        {
            account = await _accessApi.GetAccountAsync();
        }
        catch (ApiException e) when (e.StatusCode is (int)HttpStatusCode.NotFound or (int)HttpStatusCode.Unauthorized)
        {
            Console.Write(
                "You account is not registered, to create a registration call ",
                "sform account register ".ThemedHighlight(Console.Theme),
                "command.");
            return ExitCodes.WARNING_INTERACTION_NEEDED;
        }

        var accessPoints = account.AccessMap;

        Console.WriteHeader("Access points");

        foreach (var accessPoint in accessPoints)
        {
            Console.WriteLine($"- {accessPoint.Key} ", $"[{accessPoint.Value:G}]".ThemedLowlight(Console.Theme));
        }

        return ExitCodes.SUCCESS;
    }
}

