using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.KeyValues.ClientEncryption;
using _42.Platform.Storyteller.Sdk;
using _42.Platform.Storyteller.Sdk.ClientSideEncryption;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

// Adding a recipient does not give it access to existing hyok values: someone with an existing recipient's private key
// decrypts them here and encrypts them again for the current recipients, one new version per key.
[Command(CommandNames.REENCRYPT, Description = "Encrypt client-encrypted (hyok) values again for the current recipients, with an existing recipient's private key.")]
public class KeyValueReencryptCommand : KeyValueBulkCommandBase
{
    private readonly IFileSystem _fileSystem;

    public KeyValueReencryptCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues, IFileSystem fileSystem)
        : base(console, context, keyValues)
    {
        _fileSystem = fileSystem;
    }

    [Option("--private-key", CommandOptionType.SingleValue, Description = "A PEM file with the RSA private key of a current recipient.")]
    public string? PrivateKeyPath { get; set; }

    [Option("--kid", CommandOptionType.SingleValue, Description = "The recipient id the private key belongs to.")]
    public string? RecipientId { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        if (string.IsNullOrWhiteSpace(PrivateKeyPath) || string.IsNullOrWhiteSpace(RecipientId))
        {
            Console.WriteImportant("Both --private-key and --kid are required.");
            throw new WrongInputException();
        }

        var request = BuildRequest();
        using var rsa = RSA.Create();
        rsa.ImportFromPem(KeyValuePrivateKeys.Read(_fileSystem, PrivateKeyPath));

        var recipients = await KeyValues.GetRecipientsAsync(Context.OrganizationName, Context.ProjectName);
        var keys = HasKeys ? request.Keys!.ToList() : await ListClientEncryptedKeysAsync(request.KeyPrefix ?? string.Empty);
        var done = 0;
        var skipped = new List<string>();

        foreach (var key in keys)
        {
            var pair = await KeyValues.GetKeyValueAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key, true);

            if (!KeyValueLevels.IsClientEncrypted(pair.Protection) || pair.Value is null)
            {
                skipped.Add($"{key} (not client-encrypted)");
                continue;
            }

            if (!JweDocument.Parse(pair.Value).Recipients.Any(recipient => recipient.KeyId == RecipientId))
            {
                skipped.Add($"{key} (not encrypted for '{RecipientId}')");
                continue;
            }

            var plaintext = ClientSecretDecryptor.Decrypt(pair.Value, RecipientId, rsa);
            var document = KeyValueClientEncryption.Encrypt(plaintext, recipients, Context.OrganizationName, Context.ProjectName, Context.ViewName, key, pair.ContentType ?? "text/plain");

            await KeyValues.SetKeyValueAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                key,
                new KeyValueWrite { Protection = KeyValueWriteProtection.Hyok, Value = document, ContentType = pair.ContentType, ExpectedVersion = pair.Version });
            done++;
        }

        Console.WriteImportant($"{done} value(s) encrypted again for {recipients.Count(recipient => recipient.IsEnabled)} recipient(s).");

        foreach (var entry in skipped)
        {
            Console.WriteLine($"  skipped {entry}".ThemedLowlight(Console.Theme));
        }

        return skipped.Count == 0 ? ExitCodes.SUCCESS : ExitCodes.WARNING_PARTIAL_RESULT;
    }

    private async Task<List<string>> ListClientEncryptedKeysAsync(string prefix)
    {
        var keys = new List<string>();
        string? continuationToken = null;

        do
        {
            var page = await KeyValues.GetKeyValuesAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, prefix, nameof(KeyValueProtection.Hyok), null, null, continuationToken);
            keys.AddRange((page.Items ?? []).Select(item => item.Key));
            continuationToken = page.ContinuationToken;
        }
        while (!string.IsNullOrEmpty(continuationToken));

        return keys;
    }
}
