using System;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.KeyValues.ClientEncryption;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Command(CommandNames.GET, Description = "Get a pair; a secret's value only with --reveal. A client-encrypted value is decrypted locally with --private-key.")]
public class KeyValueGetCommand : KeyValueCommandBase
{
    private readonly IFileSystem _fileSystem;

    public KeyValueGetCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues, IFileSystem fileSystem)
        : base(console, context, keyValues)
    {
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "The key.")]
    public string Key { get; set; } = string.Empty;

    [Option("--reveal", CommandOptionType.NoValue, Description = "Return the value of a secret (secret rights needed).")]
    public bool Reveal { get; set; }

    [Option("--version", CommandOptionType.SingleValue, Description = "One stored version instead of the current one.")]
    public int? Version { get; set; }

    [Option("--deleted", CommandOptionType.NoValue, Description = "Also find a soft-deleted key.")]
    public bool IncludeDeleted { get; set; }

    [Option("--value-only", CommandOptionType.NoValue, Description = "Print the value alone, for scripts.")]
    public bool ValueOnly { get; set; }

    [Option("-e|--export", CommandOptionType.SingleValue, Description = "Save the pair as JSON into this file.")]
    public string? ExportFilePath { get; set; }

    [Option("--private-key", CommandOptionType.SingleValue, Description = "A PEM file with the recipient's RSA private key, to decrypt a client-encrypted (hyok) value locally.")]
    public string? PrivateKeyPath { get; set; }

    [Option("--kid", CommandOptionType.SingleValue, Description = "The recipient id the private key belongs to (needed when the value has several recipients).")]
    public string? RecipientId { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var key = RequireKey(Key);
        var reveal = Reveal || PrivateKeyPath is not null;

        var pair = Version.HasValue
            ? await KeyValues.GetKeyValueVersionAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key, Version.Value, reveal ? true : null)
            : await KeyValues.GetKeyValueAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key, reveal ? true : null, IncludeDeleted ? true : null);

        if (PrivateKeyPath is not null && KeyValueLevels.IsClientEncrypted(pair.Protection) && pair.Value is not null)
        {
            pair.Value = Decrypt(pair.Value);
        }

        if (ValueOnly)
        {
            if (pair.Value is null)
            {
                Console.WriteImportant(KeyValueLevels.IsSecret(pair.Protection) ? "The value is a secret; add --reveal." : "The pair has no value.");
                return ExitCodes.WARNING_UNAUTHORIZED_ACCESS;
            }

            Console.WriteLine(pair.Value);
            return ExitCodes.SUCCESS;
        }

        Console.WriteJson(pair);

        if (!string.IsNullOrWhiteSpace(ExportFilePath))
        {
            Console.WriteJsonToFile(pair, ExportFilePath, _fileSystem);
        }

        return ExitCodes.SUCCESS;
    }

    // The holder of a recipient's private key reads a hyok value here; the platform never could.
    private string Decrypt(string document)
    {
        var recipientIds = JweDocument.Parse(document).Recipients.Select(recipient => recipient.KeyId).ToList();
        var recipientId = RecipientId ?? (recipientIds.Count == 1 ? recipientIds[0] : null);

        if (recipientId is null)
        {
            Console.WriteImportant($"The value is encrypted for {recipientIds.Count} recipients ({string.Join(", ", recipientIds)}); name yours with --kid.");
            throw new WrongInputException();
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(KeyValuePrivateKeys.Read(_fileSystem, PrivateKeyPath!));

        try
        {
            return ClientSecretDecryptor.Decrypt(document, recipientId, rsa);
        }
        catch (CryptographicException exception)
        {
            Console.WriteImportant($"The value could not be decrypted with the key for '{recipientId}': {exception.Message}");
            throw new WrongInputException(exception.Message, exception);
        }
    }
}

internal static class KeyValuePrivateKeys
{
    // A path to a PEM file, or the PEM text itself.
    public static string Read(IFileSystem fileSystem, string pathOrPem)
    {
        if (pathOrPem.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            return pathOrPem;
        }

        if (!fileSystem.File.Exists(pathOrPem))
        {
            throw new WrongInputException($"The private key file '{pathOrPem}' does not exist.");
        }

        return fileSystem.File.ReadAllText(pathOrPem);
    }
}
