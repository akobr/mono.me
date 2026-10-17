using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

// Without a subcommand it lists the trusted issuers, like 'list'.
[Subcommand(
    typeof(MachineIssuerListCommand),
    typeof(MachineIssuerSetCommand),
    typeof(MachineIssuerDeleteCommand))]

[Command(CommandNames.ISSUER, CommandNames.ISSUERS, Description = "List and manage the trusted issuers of a project: the OIDC issuers whose tokens its federated machines exchange for Storyteller tokens.")]
public class MachineIssuerCommand : FederationCommandBase
{
    public MachineIssuerCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi)
        : base(console, context, accessApi)
    {
    }

    protected override Task<int> ExecuteFederationAsync()
    {
        return MachineIssuerListCommand.WriteIssuersAsync(Console, Context, AccessApi);
    }
}
