using System;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Command(CommandNames.IMPORT, Description = "Import pairs from a key/value document (json), a flat JSON object (flat) or a .env file (env): the plan is shown first, then confirmed.")]
public class KeyValueImportCommand : KeyValueCommandBase
{
    private readonly IFileSystem _fileSystem;

    public KeyValueImportCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues, IFileSystem fileSystem)
        : base(console, context, keyValues)
    {
        _fileSystem = fileSystem;
    }

    [Argument(0, Description = "The file to import.")]
    public string File { get; set; } = string.Empty;

    [Option("--format", CommandOptionType.SingleValue, Description = "json, flat or env; guessed from the file when missing.")]
    public string? Format { get; set; }

    [Option("--secret", CommandOptionType.MultipleValue, Description = "Keys matching this glob (* and ?) are secrets, in flat and env files (repeatable).")]
    public string[]? SecretGlobs { get; set; }

    [Option("--protection", CommandOptionType.SingleValue, Description = "The level of the secrets; the project's default secret level when missing.")]
    public string? Protection { get; set; }

    [Option("--mode", CommandOptionType.SingleValue, Description = "merge (default): write the listed keys; replace: also delete the other active keys (under --prefix, when given).")]
    public string? Mode { get; set; }

    [Option("--prefix", CommandOptionType.SingleValue, Description = "With replace: only the keys under this prefix are replaced.")]
    public string? Prefix { get; set; }

    [Option("--dry-run", CommandOptionType.NoValue, Description = "Show the plan and write nothing.")]
    public bool DryRun { get; set; }

    [Option("-y|--yes", CommandOptionType.NoValue, Description = "Do not ask for a confirmation.")]
    public bool Yes { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        if (string.IsNullOrWhiteSpace(File) || !_fileSystem.File.Exists(File))
        {
            Console.WriteImportant(string.IsNullOrWhiteSpace(File) ? "Please give the file to import." : $"The file '{File}' does not exist.");
            throw new WrongInputException();
        }

        var mode = ParseMode();
        var content = await _fileSystem.File.ReadAllTextAsync(File);
        var format = ResolveFormat(content);
        var secretLevel = await ResolveSecretLevelAsync(format);

        KeyValueDocument document;

        try
        {
            document = KeyValueFormats.Parse(content, format, key => KeyGlob.IsMatch(key, SecretGlobs) ? secretLevel!.Value : KeyValueDocumentItemProtection.Public);
        }
        catch (Exception exception) when (exception is FormatException or Newtonsoft.Json.JsonException)
        {
            Console.WriteImportant($"The file could not be read as {format.ToString().ToLowerInvariant()}: {exception.Message}");
            throw new WrongInputException(exception.Message, exception);
        }

        Console.WriteHeader($"Import of {document.Items?.Count ?? 0} pair(s) from {File} into {Context.OrganizationName}.{Context.ProjectName} / {Context.ViewName} ({mode})");

        var plan = await KeyValues.ImportKeyValuesAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, mode, true, Prefix, document);
        var planCode = Console.WriteImportResult(plan);

        if (DryRun || planCode == ExitCodes.WARNING_NO_WORK_NEEDED)
        {
            return planCode;
        }

        var changes = plan.Items?.Count(item => item.Action != KeyValueImportItemResultAction.Unchanged) ?? 0;

        if (changes == 0)
        {
            Console.WriteLine("Nothing to change.");
            return ExitCodes.WARNING_NO_WORK_NEEDED;
        }

        if (!Yes && !await ConfirmAsync(plan))
        {
            Console.WriteLine("Aborted, nothing was written.");
            return ExitCodes.WARNING_ABORTED;
        }

        var result = await KeyValues.ImportKeyValuesAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, mode, null, Prefix, document);
        Console.WriteLine();
        return Console.WriteImportResult(result);
    }

    private Task<bool> ConfirmAsync(KeyValueImportResult plan)
    {
        var deletes = plan.Items?.Count(item => item.Action == KeyValueImportItemResultAction.Delete) ?? 0;

        if (deletes > 0)
        {
            var typed = Console.Input<string>($"The import deletes {deletes} key(s). Type the view name '{Context.ViewName}' to go on");
            return Task.FromResult(string.Equals(typed?.Trim(), Context.ViewName, StringComparison.OrdinalIgnoreCase));
        }

        return Task.FromResult(Console.Confirm("Apply the plan?", true));
    }

    private string ParseMode()
    {
        switch (Mode?.Trim().ToLowerInvariant())
        {
            case null:
            case "":
            case "merge":
                return "Merge";
            case "replace":
                return "Replace";
            default:
                Console.WriteImportant($"Unknown mode '{Mode}'; use merge or replace.");
                throw new WrongInputException();
        }
    }

    private KeyValueFileFormat ResolveFormat(string content)
    {
        if (Format is null)
        {
            return KeyValueFormats.Detect(File, content);
        }

        if (KeyValueFormats.TryParseFormat(Format, out var parsed))
        {
            return parsed;
        }

        Console.WriteImportant($"Unknown format '{Format}'; use json, flat or env.");
        throw new WrongInputException();
    }

    // Flat and env files carry no levels: keys matching --secret get the given level, or the project's default secret level.
    private async Task<KeyValueDocumentItemProtection?> ResolveSecretLevelAsync(KeyValueFileFormat format)
    {
        if (format == KeyValueFileFormat.Json || SecretGlobs is not { Length: > 0 })
        {
            return null;
        }

        if (Protection is not null)
        {
            return KeyValueLevels.To<KeyValueDocumentItemProtection>(KeyValueLevels.Parse(Console, Protection));
        }

        var policy = await KeyValues.GetKeyValuePolicyAsync(Context.OrganizationName, Context.ProjectName);
        return KeyValueLevels.To<KeyValueDocumentItemProtection>(KeyValueLevels.Parse(Console, policy.DefaultSecretProtection.ToString()));
    }
}
