using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

[Command(CommandNames.MACHINE_AUTH, Description = "Set the machine authentication policy for a project.")]
public class MachineAuthSetCommand : BaseContextCommand
{
    private readonly AccessApiClient _accessApi;

    public MachineAuthSetCommand(
        IExtendedConsole console,
        ICommandContext context,
        AccessApiClient accessApi)
        : base(console, context)
    {
        _accessApi = accessApi;
    }

    [Option("-k|--credential-kind", CommandOptionType.SingleValue, Description = "Required credential kind: ApiKey, Certificate, or CertificateAndApiKey.")]
    public string CredentialKind { get; set; } = "ApiKey";

    [Option("-l|--lifetime-days", CommandOptionType.SingleValue, Description = "Default certificate lifetime in days for this project.")]
    public int? CertificateLifetimeDays { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var accessPointKey = $"{Context.OrganizationName}.{Context.ProjectName}";

        var policy = new MachineAuthenticationPolicyDto
        {
            CredentialKind = CredentialKind,
            CertificateLifetimeDays = CertificateLifetimeDays,
        };

        await _accessApi.SetMachineAuthenticationPolicyAsync(accessPointKey, policy);

        Console.WriteImportant($"Machine authentication policy set for {accessPointKey}:");
        Console.WriteLine($"  Credential kind: {CredentialKind}");

        if (CertificateLifetimeDays.HasValue)
        {
            Console.WriteLine($"  Certificate lifetime: {CertificateLifetimeDays} days");
        }

        return ExitCodes.SUCCESS;
    }
}
