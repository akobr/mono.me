using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.RESET, Description = "Reset secret for a machine access (for contributors).", ExtendedHelpText = HelpDetail)]
public class MachineResetCommand : BaseContextCommand
{
    // After the options of 'sform account machine reset --help', as it is: the empty first line separates it from
    // them, and no line is longer than 79 characters.
    internal const string HelpDetail = """

        A user needs the Contributor role on the project; for a machine with a secrets
        scope the ContributorWithSecrets role, and with a management scope the
        Administrator role. A machine cannot reset a secret, whatever its scopes.
        """;

    private readonly IAccessApiClient _accessApi;

    public MachineResetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi)
        : base(console, context)
    {
        _accessApi = accessApi;
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

        _42.Platform.Storyteller.Sdk.MachineAccess machine;

        try
        {
            machine = await _accessApi.ResetMachineAccessAsync(
                Context.OrganizationName,
                Context.ProjectName,
                MachineId);
        }
        catch (ApiException exception)
        {
            // for example a federated machine, which has no secret to reset
            return Console.WriteApiError(exception);
        }

        Console.WriteJson(machine);
        Console.WriteLine();
        Console.WriteImportant("Make sure to copy the access key (secret), it is not stored anywhere.");
        return ExitCodes.SUCCESS;
    }
}
