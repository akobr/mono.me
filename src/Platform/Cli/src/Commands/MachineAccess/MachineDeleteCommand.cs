using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using ApiSdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete a machine access.")]
public class MachineDeleteCommand : BaseContextCommand
{
    private readonly ApiClient _apiClient;

    public MachineDeleteCommand(
        IExtendedConsole console,
        ICommandContext context,
        ApiClient apiClient)
        : base(console, context)
    {
        _apiClient = apiClient;
    }

    [Argument(0, Description = "An id of an machine access.")]
    public string MachineId { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(MachineId))
        {
            Console.WriteImportant("The machine id parameter is required.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        await _apiClient.V1[Context.OrganizationName][Context.ProjectName].Access.Machines[MachineId].DeleteAsync();

        Console.WriteImportant($"The machine access {MachineId} has been deleted.");
        return ExitCodes.SUCCESS;
    }
}
