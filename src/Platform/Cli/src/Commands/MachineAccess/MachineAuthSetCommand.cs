using System;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.MACHINE_AUTH, Description = "Set the machine authentication policy for a project.")]
public class MachineAuthSetCommand : BaseContextCommand
{
    private readonly IAccessApiClient _accessApi;

    public MachineAuthSetCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi)
        : base(console, context)
    {
        _accessApi = accessApi;
    }

    [Option("-k|--credential-kind", CommandOptionType.SingleValue, Description = "Required credential kind: ApiKey, Certificate, CertificateAndApiKey, or ClientCredentials (AuthKit M2M).")]
    public string? CredentialKind { get; set; }

    [Option("-l|--lifetime-days", CommandOptionType.SingleValue, Description = "Default certificate lifetime in days for this project.")]
    public int? CertificateLifetimeDays { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        // client-credentials is accepted for ClientCredentials, like other kebab-case CLI values.
        if (string.IsNullOrWhiteSpace(CredentialKind)
            || !Enum.TryParse<MachineAuthenticationPolicyCredentialKind>(
                    CredentialKind.Replace("-", string.Empty), ignoreCase: true, out var credentialKind)
            || !Enum.IsDefined(credentialKind))
        {
            Console.WriteImportant(
                "A valid --credential-kind is required: ApiKey, Certificate, CertificateAndApiKey, or ClientCredentials.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        var accessPointKey = $"{Context.OrganizationName}.{Context.ProjectName}";

        var policy = new MachineAuthenticationPolicy
        {
            CredentialKind = credentialKind,
            CertificateLifetimeDays = CertificateLifetimeDays,
        };

        var result = await _accessApi.SetMachineAuthenticationAsync(accessPointKey, policy);

        Console.WriteImportant($"Machine authentication policy set for {accessPointKey}:");
        Console.WriteLine($"  Credential kind: {result.CredentialKind}");

        if (result.CertificateLifetimeDays.HasValue)
        {
            Console.WriteLine($"  Certificate lifetime: {result.CertificateLifetimeDays} days");
        }

        return ExitCodes.SUCCESS;
    }
}
