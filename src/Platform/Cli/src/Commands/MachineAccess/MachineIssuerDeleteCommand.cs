using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

// The kill switch of a trusted issuer: allowed while rules reference it.
[Command(CommandNames.DELETE, CommandNames.REMOVE, Description = "Delete a trusted issuer (for administrators).", ExtendedHelpText = HelpDetail)]
public class MachineIssuerDeleteCommand : FederationCommandBase
{
    // After the options of 'sform account machine issuer delete --help', as it is: the empty first line separates it
    // from them, and no line is longer than 79 characters.
    internal const string HelpDetail = """

        The issuer is deleted even while federation rules reference it: it is the
        kill switch. Those rules stop matching within a minute, so their machines can
        no longer exchange tokens of the issuer.

        A user needs the Administrator role on the project. A machine cannot delete a
        trusted issuer, whatever its scopes.
        """;

    public MachineIssuerDeleteCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi)
        : base(console, context, accessApi)
    {
    }

    [Argument(0, Description = "The name of the trusted issuer.")]
    public string Name { get; set; } = string.Empty;

    protected override async Task<int> ExecuteFederationAsync()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            Console.WriteImportant("The name parameter is required.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        try
        {
            await AccessApi.DeleteFederatedIssuerAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Name.Trim());
        }
        catch (ApiException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteImportant($"The project {Context.OrganizationName}.{Context.ProjectName} has no trusted issuer {Name}.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteImportant($"The trusted issuer {Name} has been deleted.");
        Console.WriteLine("Rules that reference it stop matching within a minute; Storyteller tokens already issued stay valid until they expire.".ThemedLowlight(Console.Theme));
        return ExitCodes.SUCCESS;
    }
}
