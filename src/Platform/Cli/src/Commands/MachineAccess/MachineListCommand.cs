using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using ApiSdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Subcommand(
    typeof(MachineCreateCommand),
    typeof(MachineResetCommand),
    typeof(MachineDeleteCommand))]

[Command(CommandNames.MACHINE, CommandNames.AGENT, Description = "Get and manage machine access to 2S platform.")]
public class MachineListCommand : BaseContextCommand
{
    private readonly ApiClient _apiClient;

    public MachineListCommand(
        IExtendedConsole console,
        ICommandContext context,
        ApiClient apiClient)
        : base(console, context)
    {
        _apiClient = apiClient;
    }

    protected override async Task<int> ExecuteAsync()
    {
        var accesses = await _apiClient.V1[Context.OrganizationName][Context.ProjectName].Access.Machines.GetAsync();

        Console.WriteHeader("Machine accesses");

        if (accesses == null || accesses.Count < 1)
        {
            Console.WriteLine("No machine access granted, yet.".ThemedLowlight(Console.Theme));
            return ExitCodes.SUCCESS;
        }

        Console.WriteTable(
            accesses,
            machine => new[] { machine.Id ?? string.Empty, $"{machine.Scope:G}", machine.AccessKey ?? string.Empty, machine.AnnotationKey ?? string.Empty },
            new[] { "Id", "Role", "Secret", "RestrictionTo" });
        return ExitCodes.SUCCESS;
    }
}
