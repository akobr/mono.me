using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete a machine access.")]
public class MachineDeleteCommand : BaseContextCommand
{
    private readonly IAccessApiAsync _accessApi;

    public MachineDeleteCommand(
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
        await _accessApi.DeleteMachineAccessAsync(
            Context.OrganizationName,
            Context.ProjectName,
            MachineId);
        return ExitCodes.SUCCESS;
    }
}
