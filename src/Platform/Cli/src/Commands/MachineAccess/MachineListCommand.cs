using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Subcommand(
    typeof(MachineCreateCommand),
    typeof(MachineResetCommand),
    typeof(MachineDeleteCommand),
    typeof(MachineRenewCommand),
    typeof(MachineAuthSetCommand),
    typeof(MachineRulesCommand),
    typeof(MachineIssuerCommand))]

[Command(CommandNames.MACHINE, CommandNames.AGENT, Description = "Get and manage machine access to 2S platform.")]
public class MachineListCommand : BaseContextCommand
{
    private readonly IAccessApiClient _accessApi;

    public MachineListCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi)
        : base(console, context)
    {
        _accessApi = accessApi;
    }

    protected override async Task<int> ExecuteAsync()
    {
        var accesses = await _accessApi.GetMachineAccessesAsync(
            Context.OrganizationName,
            Context.ProjectName);

        Console.WriteHeader("Machine accesses");

        if (accesses.Count < 1)
        {
            Console.WriteLine("No machine access granted, yet.".ThemedLowlight(Console.Theme));
            return ExitCodes.SUCCESS;
        }

        Console.WriteTable(
            accesses,
            machine => new[] { machine.Id, $"{machine.Scope:G}", $"{machine.CredentialKind:G}", DescribeSecret(machine), machine.AnnotationKey },
            new[] { "Id", "Role", "Kind", "Secret", "RestrictionTo" });
        return ExitCodes.SUCCESS;
    }

    // A federated machine has no secret, nor does a certificate machine (an empty access key).
    private static string DescribeSecret(_42.Platform.Storyteller.Sdk.MachineAccess machine)
    {
        return machine.CredentialKind == MachineAccessCredentialKind.Federated || string.IsNullOrEmpty(machine.AccessKey)
            ? "–"
            : machine.AccessKey;
    }
}
