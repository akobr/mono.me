using System.IO.Abstractions;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

// Without rule options it shows the rules of the machine. With them it replaces every rule: one rule from
// --issuer, --audience, --subject and --claim, or the rules of --rules-file.
[Command(CommandNames.RULES, Description = "Show or replace the federation rules of a federated machine.")]
public class MachineRulesCommand : FederationCommandBase
{
    private readonly IFileSystem _fileSystem;

    public MachineRulesCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IFileSystem fileSystem)
        : base(console, context, accessApi)
    {
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "The id of the federated machine.")]
    public string MachineId { get; set; } = string.Empty;

    [Option("--issuer", CommandOptionType.SingleValue, Description = "The name of the trusted issuer of the rule (sform account machine issuer).")]
    public string? FederationIssuer { get; set; }

    [Option("--audience", CommandOptionType.SingleValue, Description = "The aud the platform token must have: the machine's token endpoint URL. " + FederationRuleOptions.AzureAudienceHelp)]
    public string? FederationAudience { get; set; }

    [Option("--subject", CommandOptionType.SingleValue, Description = "The sub the platform token must have; a trailing :* or /* matches a prefix.")]
    public string? FederationSubject { get; set; }

    [Option("--claim", CommandOptionType.MultipleValue, Description = "A further claim the token must carry: key=value, the key a claim name or a JSON pointer (/kubernetes.io/namespace). Repeat it; values of one key are alternatives.")]
    public string[]? FederationClaims { get; set; }

    [Option("--rules-file", CommandOptionType.SingleValue, Description = "A JSON file with an array of rules ({ Issuer, Audience, Subject, Claims }) instead of --issuer, --audience and --subject.")]
    public string? RulesFile { get; set; }

    protected override async Task<int> ExecuteFederationAsync()
    {
        if (string.IsNullOrWhiteSpace(MachineId))
        {
            Console.WriteImportant("The machine id parameter is required.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        var options = new FederationRuleOptions(FederationIssuer, FederationAudience, FederationSubject, FederationClaims, RulesFile);

        if (!options.IsGiven)
        {
            return await WriteCurrentRulesAsync();
        }

        if (!options.TryRead(_fileSystem, out var rules, out var error))
        {
            Console.WriteImportant(error);
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        _42.Platform.Storyteller.Sdk.MachineAccess machine;

        try
        {
            machine = await AccessApi.SetMachineFederationRulesAsync(
                Context.OrganizationName,
                Context.ProjectName,
                MachineId,
                rules);
        }
        catch (ApiException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return WriteNotFound();
        }

        Console.WriteImportant($"The federation rules of {MachineId} have been replaced; they apply to new token exchanges.");
        Console.WriteFederatedMachine(machine, FederationConsoleExtensions.GetTokenEndpoint(machine, Context.OrganizationName, Context.ProjectName));
        return ExitCodes.SUCCESS;
    }

    private async Task<int> WriteCurrentRulesAsync()
    {
        _42.Platform.Storyteller.Sdk.MachineAccess machine;

        try
        {
            machine = await AccessApi.GetMachineAccessAsync(
                Context.OrganizationName,
                Context.ProjectName,
                MachineId);
        }
        catch (ApiException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            return WriteNotFound();
        }

        if (machine.CredentialKind != MachineAccessCredentialKind.Federated)
        {
            Console.WriteImportant($"The machine {MachineId} is {machine.CredentialKind:G}; only federated machines have federation rules.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteFederatedMachine(machine, FederationConsoleExtensions.GetTokenEndpoint(machine, Context.OrganizationName, Context.ProjectName));
        return ExitCodes.SUCCESS;
    }

    private int WriteNotFound()
    {
        Console.WriteImportant($"The machine access {MachineId} doesn't exist in {Context.OrganizationName}.{Context.ProjectName}.");
        return ExitCodes.ERROR_WRONG_INPUT;
    }
}
