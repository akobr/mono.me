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

[Command(CommandNames.ADD, Description = "Register a recipient: a name and an RSA public key (3072 bits or more) as a PEM or JWK file, or inline.")]
public class KeyValueRecipientAddCommand : KeyValueCommandBase
{
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

[Command(CommandNames.ENABLE, Description = "Enable a recipient.")]
public class KeyValueRecipientEnableCommand : KeyValueRecipientStateCommand
{
    public KeyValueRecipientEnableCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues)
        : base(console, context, keyValues, enabled: true)
    {
    }
}

[Command(CommandNames.DISABLE, Description = "Disable a recipient; new values are not encrypted for it.")]
public class KeyValueRecipientDisableCommand : KeyValueRecipientStateCommand
{
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

[Command(CommandNames.REMOVE, CommandNames.DELETE, Description = "Remove a recipient. Values already encrypted for it stay readable with its private key until they are encrypted again.")]
public class KeyValueRecipientRemoveCommand : KeyValueCommandBase
{
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
