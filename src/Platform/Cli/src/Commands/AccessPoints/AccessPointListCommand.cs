using System.Threading.Tasks;
using _42.CLI.Toolkit;
using _42.CLI.Toolkit.Output;
using ApiSdk;
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
    private readonly ApiClient _apiClient;

    public AccessPointListCommand(
        IExtendedConsole console,
        ApiClient apiClient)
        : base(console)
    {
        _apiClient = apiClient;
    }

    public override async Task<int> OnExecuteAsync()
    {
        ApiSdk.Models.Account? account;

        try
        {
            account = await _apiClient.V1.Access.Account.GetAsync();
        }
        catch (ApiSdk.Models.ErrorResponse)
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

        var accessPoints = account.AccessMap?.AdditionalData;

        Console.WriteHeader("Access points");

        if (accessPoints is not null)
        {
            foreach (var accessPoint in accessPoints)
            {
                Console.WriteLine($"- {accessPoint.Key} ", $"[{accessPoint.Value}]".ThemedLowlight(Console.Theme));
            }
        }

        return ExitCodes.SUCCESS;
    }
}
