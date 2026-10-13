using System;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Subcommand(
    typeof(KeyValuePolicySetCommand),
    typeof(KeyValueByokSetupCommand),
    typeof(KeyValueByokVerifyCommand),
    typeof(KeyValueRotateCommand))]

[Command(CommandNames.POLICY, Description = "Show the key/value policy of the project: allowed levels, the mTLS rule, the keys, the customer key and the recipients.")]
public class KeyValuePolicyCommand : KeyValueCommandBase
{
    public KeyValuePolicyCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var policy = await KeyValues.GetKeyValuePolicyAsync(Context.OrganizationName, Context.ProjectName);
        Console.WriteJson(policy);
        return ExitCodes.SUCCESS;
    }
}

[Command(CommandNames.SET, Description = "Change the policy (Administrator); members not given keep their value.")]
public class KeyValuePolicySetCommand : KeyValueCommandBase
{
    public KeyValuePolicySetCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Option("--allow", CommandOptionType.MultipleValue, Description = "The allowed levels (repeatable): public, standard, hsm, byok, hyok. Public is always allowed.")]
    public string[]? Allow { get; set; }

    [Option("--default", CommandOptionType.SingleValue, Description = "The default secret level clients offer.")]
    public string? Default { get; set; }

    [Option("--require-mtls", CommandOptionType.SingleValue, Description = "true or false: machines receive secrets only over mTLS.")]
    public string? RequireMtls { get; set; }

    [Option("--byok-key", CommandOptionType.SingleValue, Description = "The versionless URI of the customer's key (https://<vault>.vault.azure.net/keys/<name>); an empty value removes the settings.")]
    public string? ByokKey { get; set; }

    [Option("--byok-tenant", CommandOptionType.SingleValue, Description = "The Entra tenant id of the customer's vault.")]
    public string? ByokTenant { get; set; }

    [Option("--hsm-rotation-days", CommandOptionType.SingleValue, Description = "How often the platform rotates the HSM key.")]
    public int? HsmRotationDays { get; set; }

    [Option("--expected-version", CommandOptionType.SingleValue, Description = "Change only when the policy is at this version.")]
    public long? ExpectedVersion { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var update = new KeyValuePolicyUpdate
        {
            AllowedProtections = Allow is { Length: > 0 } ? Allow.Select(text => KeyValueLevels.To<AllowedProtections2>(KeyValueLevels.Parse(Console, text))).Distinct().ToList() : null,
            DefaultSecretProtection = Default is null ? null : KeyValueLevels.To<KeyValuePolicyUpdateDefaultSecretProtection>(KeyValueLevels.Parse(Console, Default)),
            RequireMtlsForSecretDelivery = ParseBool(RequireMtls),
            HsmRotationInterval = HsmRotationDays is null ? null : TimeSpan.FromDays(HsmRotationDays.Value).ToString("c"),
            Byok = ByokKey is null && ByokTenant is null ? null : new ByokSettingsUpdate { KeyUri = ByokKey, TenantId = ByokTenant },
            ExpectedVersion = ExpectedVersion,
        };

        if (update.AllowedProtections is null && update.DefaultSecretProtection is null && update.RequireMtlsForSecretDelivery is null
            && update.HsmRotationInterval is null && update.Byok is null)
        {
            Console.WriteImportant("Nothing to change; give at least one of --allow, --default, --require-mtls, --byok-key, --byok-tenant or --hsm-rotation-days.");
            throw new WrongInputException();
        }

        var policy = await KeyValues.SetKeyValuePolicyAsync(Context.OrganizationName, Context.ProjectName, update);
        Console.WriteImportant($"The policy of {Context.OrganizationName}.{Context.ProjectName} is at version {policy.Version}.");
        Console.WriteJson(policy);
        return ExitCodes.SUCCESS;
    }

    private bool? ParseBool(string? text)
    {
        if (text is null)
        {
            return null;
        }

        if (bool.TryParse(text, out var value))
        {
            return value;
        }

        Console.WriteImportant($"'{text}' is not true or false.");
        throw new WrongInputException();
    }
}

[Command(CommandNames.BYOK_SETUP, Description = "What the customer needs to bring their own key: the application to consent to and the role to grant.")]
public class KeyValueByokSetupCommand : KeyValueCommandBase
{
    public KeyValueByokSetupCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var setup = await KeyValues.GetByokSetupAsync(Context.OrganizationName, Context.ProjectName);

        Console.WriteHeader("Bring your own key");
        Console.WriteLine("1. An administrator of your tenant consents to the platform's application: ", setup.ConsentUrl.ThemedHighlight(Console.Theme));
        Console.WriteLine("2. Grant its service principal the role ", setup.RequiredRole.ThemedHighlight(Console.Theme), " on your key, and let the platform reach the vault (a private endpoint, or a firewall rule).");
        Console.WriteLine("3. Set the key here: ", $"sform story kv policy set --byok-key https://<vault>.vault.azure.net/keys/<name> --byok-tenant <tenant id>".ThemedHighlight(Console.Theme));
        Console.WriteLine("4. Verify it: ", "sform story kv policy byok-verify".ThemedHighlight(Console.Theme), ", then allow the level with ", "--allow byok".ThemedHighlight(Console.Theme));
        Console.WriteLine();
        Console.WriteLine("Application id: ".ThemedLowlight(Console.Theme), setup.ClientId);
        return ExitCodes.SUCCESS;
    }
}

[Command(CommandNames.BYOK_VERIFY, Description = "Verify the customer key set in the policy (enabled, RSA 2048+, wrap and unwrap allowed, a round trip works).")]
public class KeyValueByokVerifyCommand : KeyValueCommandBase
{
    public KeyValueByokVerifyCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var policy = await KeyValues.VerifyByokAsync(Context.OrganizationName, Context.ProjectName);
        var byok = policy.Byok;

        Console.WriteImportant(byok is null
            ? "No customer key is set."
            : $"The customer key {byok.KeyUri} is {byok.Health} (version {byok.CurrentKeyVersion}, verified {KeyValueTimes.Format(byok.VerifiedAt)}).");
        return ExitCodes.SUCCESS;
    }
}

[Command(CommandNames.ROTATE, Description = "Rotate the project's standard or hsm key-encryption key now and start the re-wrap of the stored values.")]
public class KeyValueRotateCommand : KeyValueCommandBase
{
    public KeyValueRotateCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Option("--protection", CommandOptionType.SingleValue, Description = "standard or hsm (the customer rotates a byok key in their own vault).")]
    public string? Protection { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var level = KeyValueLevels.Parse(Console, Protection);

        if (level is not (KeyValueProtection.Standard or KeyValueProtection.Hsm))
        {
            Console.WriteImportant("Only standard and hsm keys are rotated by the platform.");
            throw new WrongInputException();
        }

        var policy = await KeyValues.RotateKeyValueKeyAsync(
            Context.OrganizationName,
            Context.ProjectName,
            new KeyValueRotateRequest { Protection = KeyValueLevels.To<KeyValueRotateRequestProtection>(level) });

        var key = level == KeyValueProtection.Hsm ? policy.Hsm?.Key : policy.Standard;
        Console.WriteImportant($"The {level} key is at version {key?.CurrentKeyVersion ?? "?"}; the re-wrap of the stored values has started.");

        if (policy.Rewrap is not null)
        {
            Console.WriteLine($"Re-wrap: {policy.Rewrap.Processed} processed, {policy.Rewrap.Remaining} remaining; follow it with 'sform story kv policy'.".ThemedLowlight(Console.Theme));
        }

        return ExitCodes.SUCCESS;
    }
}
