using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Subcommand(
    typeof(KeyValueRecipientAddCommand),
    typeof(KeyValueRecipientEnableCommand),
    typeof(KeyValueRecipientDisableCommand),
    typeof(KeyValueRecipientRemoveCommand))]

[Command(CommandNames.RECIPIENTS, Description = "The recipients of client-encrypted (hyok) values: the RSA public keys the clients encrypt for.")]
public class KeyValueRecipientsCommand : KeyValueCommandBase
{
    public KeyValueRecipientsCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var recipients = await KeyValues.GetRecipientsAsync(Context.OrganizationName, Context.ProjectName);
        Console.WriteHeader($"Recipients of {Context.OrganizationName}.{Context.ProjectName}");
        Console.WriteRecipients(recipients);
        return ExitCodes.SUCCESS;
    }
}

[Command(CommandNames.ADD, Description = "Register a recipient (for administrators).", ExtendedHelpText = HelpDetail)]
public class KeyValueRecipientAddCommand : KeyValueCommandBase
{
    // After the options of 'sform story kv recipients add --help', as it is: the empty first line separates it from
    // them, and no line is longer than 79 characters.
    internal const string HelpDetail = """

        A recipient is a name (--name) and an RSA public key (3072 bits or more) as a
        PEM or JWK file, or inline (--public-key). Existing hyok values are not
        encrypted for it until 'sform story kv reencrypt' encrypts them again.

        A user needs the Administrator role on the project. A machine cannot change the
        recipients, whatever its scopes.
        """;

    private readonly IFileSystem _fileSystem;

    public KeyValueRecipientAddCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues, IFileSystem fileSystem)
        : base(console, context, keyValues)
    {
        _fileSystem = fileSystem;
    }

    [Option("--name", CommandOptionType.SingleValue, Description = "A name for the recipient.")]
    public string? Name { get; set; }

    [Option("--public-key", CommandOptionType.SingleValue, Description = "A file with the public key (PEM or JWK), or the key itself.")]
    public string? PublicKey { get; set; }

    [Option("--id", CommandOptionType.SingleValue, Description = "The recipient id (the JWE kid); generated when missing.")]
    public string? Id { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(PublicKey))
        {
            Console.WriteImportant("Both --name and --public-key are required.");
            throw new WrongInputException();
        }

        var publicKey = _fileSystem.File.Exists(PublicKey) ? await _fileSystem.File.ReadAllTextAsync(PublicKey) : PublicKey;
        var recipient = await KeyValues.AddRecipientAsync(
            Context.OrganizationName,
            Context.ProjectName,
            new ClientEncryptionRecipientCreate { Id = Id, Name = Name, PublicKey = publicKey });

        Console.WriteImportant($"Recipient '{recipient.Id}' ({recipient.Name}) added.");
        Console.WriteLine("Existing hyok values are not encrypted for it; run 'sform story kv reencrypt' with an existing recipient's private key.".ThemedLowlight(Console.Theme));
        return ExitCodes.SUCCESS;
    }
}

[Command(CommandNames.ENABLE, Description = "Enable a recipient (for administrators).")]
public class KeyValueRecipientEnableCommand : KeyValueRecipientStateCommand
{
    public KeyValueRecipientEnableCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues, enabled: true)
    {
    }
}

[Command(CommandNames.DISABLE, Description = "Disable a recipient (for administrators).", ExtendedHelpText = HelpDetail)]
public class KeyValueRecipientDisableCommand : KeyValueRecipientStateCommand
{
    // After the options of 'sform story kv recipients disable --help', as it is: the empty first line separates it
    // from them, and no line is longer than 79 characters.
    internal const string HelpDetail = """

        New values are not encrypted for a disabled recipient, until 'sform story kv
        recipients enable' enables it again.

        A user needs the Administrator role on the project. A machine cannot change the
        recipients, whatever its scopes.
        """;

    public KeyValueRecipientDisableCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues, enabled: false)
    {
    }
}

public abstract class KeyValueRecipientStateCommand : KeyValueCommandBase
{
    private readonly bool _enabled;

    protected KeyValueRecipientStateCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues, bool enabled)
        : base(console, context, keyValues)
    {
        _enabled = enabled;
    }

    [Argument(0, Description = "The recipient id.")]
    public string Id { get; set; } = string.Empty;

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var id = RequireKey(Id);
        var recipient = await KeyValues.UpdateRecipientAsync(Context.OrganizationName, Context.ProjectName, id, new ClientEncryptionRecipientUpdate { IsEnabled = _enabled });
        Console.WriteImportant($"Recipient '{recipient.Id}' is {(recipient.IsEnabled ? "enabled" : "disabled")}.");
        return ExitCodes.SUCCESS;
    }
}

[Command(CommandNames.REMOVE, CommandNames.DELETE, Description = "Remove a recipient (for administrators).", ExtendedHelpText = HelpDetail)]
public class KeyValueRecipientRemoveCommand : KeyValueCommandBase
{
    // After the options of 'sform story kv recipients remove --help', as it is: the empty first line separates it
    // from them, and no line is longer than 79 characters.
    internal const string HelpDetail = """

        Values already encrypted for it stay readable with its private key until they
        are encrypted again without it ('sform story kv reencrypt'), which writes new
        versions: older versions stay readable with it until they expire. The removal
        asks for a confirmation; -y skips it.

        A user needs the Administrator role on the project. A machine cannot change the
        recipients, whatever its scopes.
        """;

    public KeyValueRecipientRemoveCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues)
    {
    }

    [Argument(0, Description = "The recipient id.")]
    public string Id { get; set; } = string.Empty;

    [Option("-y|--yes", CommandOptionType.NoValue, Description = "Do not ask for a confirmation.")]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var id = RequireKey(Id);

        if (!Yes)
        {
            Console.EnsureInteractive("the confirmation of the removal", "Pass -y|--yes to remove the recipient without asking.");
        }

        if (!Yes && !Console.Confirm($"Remove the recipient '{id}'? Values encrypted for it stay readable with its private key.", false))
        {
            Console.WriteLine("Aborted.");
            return ExitCodes.WARNING_ABORTED;
        }

        await KeyValues.RemoveRecipientAsync(Context.OrganizationName, Context.ProjectName, id);
        Console.WriteImportant($"Recipient '{id}' removed.");
        return ExitCodes.SUCCESS;
    }
}
