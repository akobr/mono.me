using System;
using System.IO;
using System.IO.Abstractions;
using System.Net;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Newtonsoft.Json;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.Commands.Projects;

[Subcommand(
    typeof(ProjectSettingsSetCommand),
    typeof(ProjectSettingsEditCommand),
    typeof(ProjectSettingsResetCommand))]

[Command(CommandNames.SETTINGS, Description = "Show the settings of the project that every member sees: the names, colors and icons of its annotation types, its default time zone and its date format.")]
public class ProjectSettingsCommand : BaseContextCommand
{
    internal const string TextFormat = "text";
    internal const string JsonFormat = "json";

    private readonly IAccessApiClient _accessApi;
    private readonly IFileSystem _fileSystem;
    private readonly OutputSwitches _switches;

    public ProjectSettingsCommand(
        IExtendedConsole console,
        ICommandContext context,
        IAccessApiClient accessApi,
        IFileSystem fileSystem,
        OutputSwitches switches)
        : base(console, context)
    {
        _accessApi = accessApi;
        _fileSystem = fileSystem;
        _switches = switches;
    }

    [Option("-f|--format", CommandOptionType.SingleValue, ValueName = "format", Description = "text (the default): a table for people; json: the settings as the API answers them (with Version and Modified), for programs.")]
    public string? Format { get; set; }

    [Option("-e|--export", CommandOptionType.SingleValue, ValueName = "file", Description = "Also write the settings to this file in the file shape of data sets (the Format marker, without Version and Modified), which 'set -i' reads.")]
    public string? ExportFilePath { get; set; }

    protected override async Task<int> ExecuteAsync()
    {
        var isJson = string.Equals(Format?.Trim(), JsonFormat, StringComparison.OrdinalIgnoreCase);

        if (!isJson && Format is not null && !string.Equals(Format.Trim(), TextFormat, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteImportant($"Unknown format '{Format}'; use text or json.");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        SdkSettings settings;

        try
        {
            (settings, _) = await ProjectSettingsCommands.ReadAsync(_accessApi, Context);
        }
        catch (ApiException e) when (e.StatusCode != (int)HttpStatusCode.Unauthorized)
        {
            // the JSON goes to programs, so its failure is a plain line on the error output
            return isJson ? Console.WriteApiErrorLine(e) : Console.WriteApiError(e);
        }

        // A JSON export that cannot be written is refused before the JSON reaches the standard output: the file is
        // written first.
        if (isJson && !string.IsNullOrWhiteSpace(ExportFilePath)
            && ExportForPrograms(ProjectSettingsDocuments.ToFileDocument(ProjectSettingsDocuments.ToModel(settings)), ExportFilePath) is { } problem)
        {
            Console.WriteErrorLine($"The settings were not exported: {problem}");
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        if (isJson)
        {
            WriteOut(JsonConvert.SerializeObject(settings, Formatting.Indented));
            return ExitCodes.SUCCESS;
        }

        Console.WriteProjectSettings(
            ProjectSettingsCommands.ProjectKeyOf(Context),
            settings,
            Console.ShowsTypeColors(_switches.GetVariable),
            Console.ShowsTypeIcons(_switches));

        if (!string.IsNullOrWhiteSpace(ExportFilePath))
        {
            var document = ProjectSettingsDocuments.ToFileDocument(ProjectSettingsDocuments.ToModel(settings));
            Console.WriteJsonToFile(document, ExportFilePath, _fileSystem);
        }

        return ExitCodes.SUCCESS;
    }

    // Why the JSON mode cannot write the export there: a folder at the path, or a missing folder of the file.
    private string? ExportProblem(string filePath)
    {
        if (_fileSystem.Directory.Exists(filePath))
        {
            return $"'{filePath}' is a folder.";
        }

        var folder = _fileSystem.Path.GetDirectoryName(_fileSystem.Path.GetFullPath(filePath));
        return string.IsNullOrEmpty(folder) || _fileSystem.Directory.Exists(folder)
            ? null
            : $"the folder '{folder}' does not exist.";
    }

    // The export of the JSON mode, before the JSON is printed: the standard output carries the JSON only, so the file is
    // written without a prompt and its notices are plain lines on the error output. Gives why it could not be written.
    private string? ExportForPrograms(object document, string filePath)
    {
        if (ExportProblem(filePath) is { } problem)
        {
            return problem;
        }

        try
        {
            if (_fileSystem.File.Exists(filePath))
            {
                Console.WriteErrorLine($"The file '{_fileSystem.Path.GetFileName(filePath)}' already exists and is rewritten.");
            }

            _fileSystem.File.WriteAllText(filePath, JsonConvert.SerializeObject(document));
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return e.Message;
        }
    }

    // Plain text on the standard output, for programs: in UTF-8, so every label comes out as it is.
    private void WriteOut(string text)
    {
        using var encoding = Console.UseUtf8Output();
        (Console.Console?.Out ?? System.Console.Out).WriteLine(text);
    }
}
