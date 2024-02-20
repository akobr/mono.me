using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.RESET, Description = "Reset secret for an machine access.")]
public class MachineResetCommand : BaseContextCommand
{
    private readonly IAccessApiAsync _accessApi;

    public MachineResetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiAsync accessApi)
        : base(console, context)
    {
        _accessApi = accessApi;
    }

    [Argument(0, Description = "An id of an machine access.")]
    public string MachineId { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        var machine = await _accessApi.ResetMachineAccessAsync(
            Context.OrganizationName,
            Context.ProjectName,
            MachineId);

        Console.WriteJson(machine);
        return ExitCodes.SUCCESS;
    }
}
