using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.MachineAccess;

// Without options it shows the policy. With them it sets the whole policy: the enabled kinds,
// the default kind (the first enabled kind unless --default-kind says otherwise) and the default
// certificate lifetime (the deployment default when not given).
[Command(CommandNames.MACHINE_AUTH, Description = "Show or set the machine authentication policy of a project.")]
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

    [Option("-k|--credential-kind", CommandOptionType.MultipleValue, Description = "A credential kind the machines of the project may use: " + CredentialKinds.Names + ". Repeat the option or separate the kinds by commas.")]
    public string[]? CredentialKind { get; set; }

    [Option("-d|--default-kind", CommandOptionType.SingleValue, Description = "The kind a new machine gets when it names none. One of the enabled kinds; the first one by default.")]
    public string? DefaultKind { get; set; }

    [Option("-l|--lifetime-days", CommandOptionType.SingleValue, Description = "Default certificate lifetime in days for new machines of this project.")]
    public int? CertificateLifetimeDays { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var accessPointKey = $"{Context.OrganizationName}.{Context.ProjectName}";
        var values = (CredentialKind ?? [])
            .SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();

        if (values.Count < 1 && DefaultKind is null && CertificateLifetimeDays is null)
        {
            WritePolicy(accessPointKey, await _accessApi.GetMachineAuthenticationAsync(accessPointKey));
            return ExitCodes.SUCCESS;
        }

        var kinds = new List<MachineCredentialKind>();

        foreach (var value in values)
        {
            if (!CredentialKinds.TryParse<MachineCredentialKind>(value, out var kind))
            {
                Console.WriteImportant($"Unknown credential kind '{value}'. Use {CredentialKinds.Names}.");
                return ExitCodes.ERROR_INPUT_PARSING;
            }

            if (!kinds.Contains(kind))
            {
                kinds.Add(kind);
            }
        }

        if (kinds.Count < 1)
        {
            Console.WriteImportant($"At least one --credential-kind is required: {CredentialKinds.Names}.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        var defaultKind = kinds[0];

        if (DefaultKind is not null
            && (!CredentialKinds.TryParse(DefaultKind, out defaultKind) || !kinds.Contains(defaultKind)))
        {
            Console.WriteImportant($"The --default-kind has to be one of the enabled kinds: {string.Join(", ", kinds)}.");
            return ExitCodes.ERROR_INPUT_PARSING;
        }

        var policy = new MachineAuthenticationPolicy
        {
            EnabledCredentialKinds = kinds,
            CredentialKind = CredentialKinds.Convert<MachineAuthenticationPolicyCredentialKind>(defaultKind),
            CertificateLifetimeDays = CertificateLifetimeDays,
        };

        var result = await _accessApi.SetMachineAuthenticationAsync(accessPointKey, policy);
        WritePolicy(accessPointKey, result);
        return ExitCodes.SUCCESS;
    }

    private void WritePolicy(string accessPointKey, MachineAuthenticationPolicy policy)
    {
        Console.WriteImportant($"Machine authentication policy of {accessPointKey}:");
        Console.WriteLine($"  Enabled kinds:        {string.Join(", ", policy.EnabledCredentialKinds ?? [])}");
        Console.WriteLine($"  Default kind:         {policy.CredentialKind}");

        if (policy.CertificateLifetimeDays.HasValue)
        {
            Console.WriteLine($"  Certificate lifetime: {policy.CertificateLifetimeDays} days");
        }
    }
}
