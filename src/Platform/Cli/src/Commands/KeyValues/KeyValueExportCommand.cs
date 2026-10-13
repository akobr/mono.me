using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.KeyValues;

[Command(CommandNames.EXPORT, Description = "Export the pairs of the view as a key/value document (json), a flat JSON object (flat) or .env lines (env).")]
public class KeyValueExportCommand : KeyValueCommandBase
{
    private readonly IFileSystem _fileSystem;

    public KeyValueExportCommand(IExtendedConsole console, ICommandContext context, IKeyValuesApiClient keyValues, IFileSystem fileSystem)
        : base(console, context, keyValues)
    {
        _fileSystem = fileSystem;
    }

    [Option("-o|--output", CommandOptionType.SingleValue, Description = "The file to write; the standard output when missing.")]
    public string? OutputPath { get; set; }

    [Option("--format", CommandOptionType.SingleValue, Description = "json (default), flat or env; guessed from the file name when missing.")]
    public string? Format { get; set; }

    [Option("--prefix", CommandOptionType.SingleValue, Description = "Only keys under this prefix.")]
    public string? Prefix { get; set; }

    [Option("--protection", CommandOptionType.MultipleValue, Description = "Only these levels (repeatable).")]
    public string[]? Protections { get; set; }

    [Option("--include-secrets", CommandOptionType.NoValue, Description = "Include the values of secrets (secret rights needed); otherwise secrets are listed without a value.")]
    public bool IncludeSecrets { get; set; }

    protected override async Task<int> ExecuteKeyValuesAsync()
    {
        var format = ResolveFormat();
        var document = await KeyValues.ExportKeyValuesAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            new KeyValueExportRequest
            {
                KeyPrefix = Prefix,
                Protections = Protections is { Length: > 0 } ? Protections.Select(text => KeyValueLevels.To<Protections>(KeyValueLevels.Parse(Console, text))).ToList() : null,
                IncludeSecrets = IncludeSecrets,
            });

        var text = KeyValueFormats.Serialize(document, format, out var skipped);

        if (skipped.Count > 0)
        {
            Console.WriteLine($"{skipped.Count} secret(s) without a value were left out of the {format.ToString().ToLowerInvariant()} output: {string.Join(", ", skipped)}. Add --include-secrets, or use the json format.".ThemedLowlight(Console.Theme));
        }

        if (string.IsNullOrWhiteSpace(OutputPath))
        {
            Console.WriteLine(text);
            return ExitCodes.SUCCESS;
        }

        await _fileSystem.File.WriteAllTextAsync(OutputPath, text);
        Console.WriteImportant($"{document.Items?.Count ?? 0} pair(s) written to {OutputPath}.");
        return ExitCodes.SUCCESS;
    }

    private KeyValueFileFormat ResolveFormat()
    {
        if (Format is not null)
        {
            if (KeyValueFormats.TryParseFormat(Format, out var parsed))
            {
                return parsed;
            }

            Console.WriteImportant($"Unknown format '{Format}'; use json, flat or env.");
            throw new WrongInputException();
        }

        return string.IsNullOrWhiteSpace(OutputPath) ? KeyValueFileFormat.Json : KeyValueFormats.Detect(OutputPath, null);
    }
}
