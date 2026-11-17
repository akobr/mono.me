using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete a machine access (for contributors).", ExtendedHelpText = HelpDetail)]
public class MachineDeleteCommand : BaseContextCommand
{
    // After the options of 'sform account machine delete --help', as it is: the empty first line separates it from
    // them, and no line is longer than 79 characters.
    internal const string HelpDetail = """

        A user needs the Contributor role on the project; for a machine with a secrets
        scope the ContributorWithSecrets role, and with a management scope the
        Administrator role. A machine cannot delete a machine access, whatever its
        scopes.
        """;

    private readonly IAccessApiClient _accessApi;

    public MachineDeleteCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi)
        : base(console, context)
    {
        _accessApi = accessApi;
    }

    [Argument(0, Description = "The id of a machine access.")]
    public string MachineId { get; set; } = string.Empty;

    protected override async Task<int> ExecuteAsync()
    {
        if (string.IsNullOrWhiteSpace(MachineId))
        {
            Console.WriteImportant("The machine id parameter is required.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        await _accessApi.DeleteMachineAccessAsync(
            Context.OrganizationName,
            Context.ProjectName,
            MachineId);

        Console.WriteImportant($"The machine access {MachineId} has been deleted.");
        return ExitCodes.SUCCESS;
    }
}

