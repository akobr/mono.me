using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.LIST, Description = "List the trusted issuers of the project (Contributor).")]
public class MachineIssuerListCommand : FederationCommandBase
{
    public MachineIssuerListCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi)
        : base(console, context, accessApi)
    {
    }

    internal static async Task<int> WriteIssuersAsync(IExtendedConsole console, ICommandContext context, IAccessApiClient accessApi)
    {
        var issuers = await accessApi.GetFederatedIssuersAsync(
            context.OrganizationName,
            context.ProjectName);

        console.WriteHeader("Trusted issuers");
        console.WriteFederatedIssuers(issuers);
        return ExitCodes.SUCCESS;
    }

    protected override Task<int> ExecuteFederationAsync()
    {
        return WriteIssuersAsync(Console, Context, AccessApi);
    }
}
