using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Sdk.Api;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Subcommand(
    typeof(MachineCreateCommand),
    typeof(MachineResetCommand),
    typeof(MachineDeleteCommand))]

[Command(CommandNames.MACHINE, CommandNames.AGENT, Description = "Get and manage machine access to 2S platform.")]
public class MachineListCommand : BaseContextCommand
{
    private readonly IAccessApiAsync _accessApi;

    public MachineListCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiAsync accessApi)
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
            machine => new[] { machine.AuthId, $"{machine.Scope:G}", machine.AccessKey, machine.AnnotationKey },
            new[] { "Id", "Role", "Secret", "RestrictionTo" });
        return ExitCodes.SUCCESS;
    }
}
