using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using ApiSdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.RESET, Description = "Reset secret for an machine access.")]
public class MachineResetCommand : BaseContextCommand
{
    private readonly ApiClient _apiClient;

    public MachineResetCommand(
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

        var machine = await _apiClient.V1[Context.OrganizationName][Context.ProjectName].Access.Machines[MachineId].PutAsync();

        Console.WriteJson(machine);
        Console.WriteLine();
        Console.WriteImportant("Make sure to copy the access key (secret), it is not stored anywhere.");
        return ExitCodes.SUCCESS;
    }
}
