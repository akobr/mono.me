using System;
using System.IO.Abstractions;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using _42.Platform.Storyteller.Sdk.ClientSideEncryption;
using McMaster.Extensions.CommandLineUtils;
using Sharprompt;

namespace _42.Platform.Cli.Commands.KeyValues;

[Command(CommandNames.SET, CommandNames.CREATE, Description = "Create or update a pair. A secret without a value source is asked for with hidden input; a hyok value is encrypted locally for the project's recipients.")]
public class KeyValueSetCommand : KeyValueCommandBase
{
    public const string SecretReferenceContentType = "application/vnd.2s.secret-reference";

    private readonly IFileSystem _fileSystem;

    public KeyValueSetCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues, IFileSystem fileSystem)
        : base(console, context, keyValues)
    {
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "The key.")]
    public string Key { get; set; } = string.Empty;

    [Argument(1, Description = "The value. For a secret prefer --stdin, a file, or the prompt: an argument stays in the shell history.")]
    public string? Value { get; set; }

    [Option("--protection", CommandOptionType.SingleValue, Description = "The level: public, standard, hsm, byok or hyok. Required for a new key; an update keeps the level when missing.")]
    public string? Protection { get; set; }

    [Option("--secret", CommandOptionType.NoValue, Description = "A secret at the project's default secret level.")]
    public bool Secret { get; set; }

    [Option("--stdin", CommandOptionType.NoValue, Description = "Read the value from the standard input.")]
    public bool FromStdin { get; set; }

    [Option("-i|--import", CommandOptionType.SingleValue, Description = "Read the value from this file.")]
    public string? ImportFilePath { get; set; }

    [Option("--content-type", CommandOptionType.SingleValue, Description = "The content type of the value (text/plain by default).")]
    public string? ContentType { get; set; }

    [Option("--reference", CommandOptionType.NoValue, Description = "The value is a reference to a secret in your own Key Vault (kv://vault/name or the vault URI); a public pair.")]
    public bool Reference { get; set; }

    [Option("--expected-version", CommandOptionType.SingleValue, Description = "Write only when the key is at this version (0: the key must not exist).")]
    public long? ExpectedVersion { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var key = RequireKey(Key);
        var (level, effectiveLevel) = await ResolveLevelAsync(key);
        var contentType = Reference ? SecretReferenceContentType : ContentType;

        if (Reference && effectiveLevel != KeyValueProtection.Public)
        {
            Console.WriteImportant("A reference is a public pair; the platform never resolves it. Drop --protection or --secret.");
            throw new WrongInputException();
        }

        var value = await ReadValueAsync(effectiveLevel);

        if (effectiveLevel == KeyValueProtection.Hyok)
        {
            var recipients = await KeyValues.GetRecipientsAsync(Context.OrganizationName, Context.ProjectName);
            value = KeyValueClientEncryption.Encrypt(value, recipients, Context.OrganizationName, Context.ProjectName, Context.ViewName, key, contentType ?? "text/plain");
            Console.WriteLine($"Encrypted locally for {recipients.Count(recipient => recipient.IsEnabled)} recipient(s); the platform cannot read the value.".ThemedLowlight(Console.Theme));
        }

        var pair = await KeyValues.SetKeyValueAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            key,
            new KeyValueWrite
            {
                Protection = level is null ? null : KeyValueLevels.To<KeyValueWriteProtection>(level.Value),
                Value = value,
                ContentType = contentType,
                ExpectedVersion = ExpectedVersion,
            });

        Console.WriteImportant($"'{pair.Key}' is at version {pair.Version} ({pair.Protection}).");
        return ExitCodes.SUCCESS;
    }

    // The level to write (null keeps the stored one on an update) and the level the value ends up at: given, the
    // project's default secret level with --secret, Public for a new key without any, or the stored level of an existing key.
    private async Task<(KeyValueProtection? Write, KeyValueProtection Effective)> ResolveLevelAsync(string key)
    {
        if (Protection is not null)
        {
            var given = KeyValueLevels.Parse(Console, Protection);
            return (given, given);
        }

        if (Secret)
        {
            var policy = await KeyValues.GetKeyValuePolicyAsync(Context.OrganizationName, Context.ProjectName);
            var fallback = KeyValueLevels.Parse(Console, policy.DefaultSecretProtection.ToString());
            return (fallback, fallback);
        }

        if (Reference)
        {
            return (KeyValueProtection.Public, KeyValueProtection.Public);
        }

        try
        {
            var existing = await KeyValues.GetKeyValueAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, key);

            // the write keeps the stored level; the value is still read and encrypted for that level
            return (null, Enum.Parse<KeyValueProtection>(existing.Protection.ToString()));
        }
        catch (ApiException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            Console.WriteLine("New key, stored as Public; use --secret or --protection for a secret.".ThemedLowlight(Console.Theme));
            return (KeyValueProtection.Public, KeyValueProtection.Public);
        }
    }

    private async Task<string> ReadValueAsync(KeyValueProtection? level)
    {
        var sources = new[] { Value is not null, FromStdin, ImportFilePath is not null }.Count(source => source);

        if (sources > 1)
        {
            Console.WriteImportant("Give the value once: as an argument, with --stdin, or with --import.");
            throw new WrongInputException();
        }

        if (ImportFilePath is not null)
        {
            if (!_fileSystem.File.Exists(ImportFilePath))
            {
                Console.WriteImportant($"The file '{ImportFilePath}' does not exist.");
                throw new WrongInputException();
            }

            return await _fileSystem.File.ReadAllTextAsync(ImportFilePath);
        }

        if (FromStdin)
        {
            return (await Console.Console.In.ReadToEndAsync()).TrimEnd('\r', '\n');
        }

        if (Value is not null)
        {
            if (KeyValueLevels.IsSecret(level))
            {
                Console.WriteLine("A secret given as an argument stays in the shell history; prefer --stdin, --import or the prompt.".ThemedLowlight(Console.Theme));
            }

            return Value;
        }

        if (KeyValueLevels.IsSecret(level))
        {
            return Console.Password(new PasswordOptions { Message = $"Value of '{Key}'" });
        }

        Console.WriteImportant("Please give the value: as an argument, with --stdin, or with --import.");
        throw new WrongInputException();
    }
}
