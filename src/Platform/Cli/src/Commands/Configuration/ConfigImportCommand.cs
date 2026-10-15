using System;
using System.IO;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.IMPORT, Description = "Import configuration documents into the view from a zip or a folder: every <annotation-key>.json replaces the stored document as a new version unless it is equal.")]
public class ConfigImportCommand : BaseContextCommand
{
    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IFileSystem _fileSystem;
    private readonly TransferPolling _polling;

    public ConfigImportCommand(
        IExtendedConsole console,
        ICommandContext context,
        IConfigurationsApiClient configurationApi,
        IFileSystem fileSystem,
        TransferPolling polling)
        : base(console, context)
    {
        _configurationApi = configurationApi;
        _fileSystem = fileSystem;
        _polling = polling;
    }

    [Argument(0, Description = "A zip, for example an export, or a folder; its *.json files are packed, without .git folders.")]
    public string Path { get; set; } = string.Empty;

    [Option("--dry-run", CommandOptionType.NoValue, Description = "Validate and compare only; nothing is written.")]
    public bool IsDryRun { get; set; }

    [Option("--force", CommandOptionType.NoValue, Description = "Store documents even if they violate their schema.")]
    public bool IsForced { get; set; }

    [Option("--timeout", CommandOptionType.SingleValue, Description = "Minutes to wait for the import (10 by default); the import continues on the server after that.")]
    public int TimeoutMinutes { get; set; } = 10;

    protected override async Task<int> ExecuteAsync()
    {
        Stream archive;
        string fileName;

        if (!string.IsNullOrWhiteSpace(Path) && _fileSystem.Directory.Exists(Path))
        {
            var (packed, count) = ConfigurationArchives.PackFolder(_fileSystem, Path);

            if (count == 0)
            {
                packed.Dispose();
                Console.WriteLine($"The folder '{Path}' has no *.json files.".ThemedError(Console.Theme));
                return ExitCodes.ERROR_WRONG_INPUT;
            }

            archive = packed;
            fileName = $"{_fileSystem.Path.GetFileName(_fileSystem.Path.TrimEndingDirectorySeparator(_fileSystem.Path.GetFullPath(Path)))}.zip";
        }
        else if (!string.IsNullOrWhiteSpace(Path) && _fileSystem.File.Exists(Path))
        {
            archive = _fileSystem.File.OpenRead(Path);
            fileName = _fileSystem.Path.GetFileName(Path);
        }
        else
        {
            Console.WriteLine($"The path '{Path}' does not exist.".ThemedError(Console.Theme));
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        ConfigurationImport import;

        await using (archive)
        {
            try
            {
                import = await _configurationApi.StartConfigurationImportAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    IsForced ? true : null,
                    IsDryRun ? true : null,
                    fileName,
                    archive);
            }
            catch (ApiException exception) when (exception.StatusCode is 400 or 409 or 413)
            {
                Console.WriteLine(exception.GetErrorMessage().ThemedError(Console.Theme));
                return ExitCodes.ERROR_WRONG_INPUT;
            }
        }

        Console.WriteLine(
            $"Importing {fileName} into {Context.OrganizationName}.{Context.ProjectName}.{Context.ViewName}{(IsDryRun ? " (dry run)" : string.Empty)}…"
                .ThemedLowlight(Console.Theme));

        var (finished, isDone) = await _polling.WaitAsync(
            () => _configurationApi.GetConfigurationImportAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, import.Id),
            job => job.Status is ConfigurationImportStatus.Succeeded or ConfigurationImportStatus.Failed,
            TimeSpan.FromMinutes(TimeoutMinutes));

        if (!isDone)
        {
            Console.WriteLine($"The import {import.Id} is still {finished.Status.ToString().ToLowerInvariant()} ({finished.ProcessedCount ?? 0} of {finished.TotalCount ?? 0} files); it continues on the server.");
            return ExitCodes.WARNING_ABORTED;
        }

        if (finished.Status == ConfigurationImportStatus.Failed)
        {
            // the server writes the whole message, prefix included
            Console.WriteLine((finished.Error ?? "The import failed.").ThemedError(Console.Theme));
            return ExitCodes.ERROR_CRASH;
        }

        Console.WriteHeader($"Import of {fileName}");
        Console.WriteImport(finished);
        return finished.Summary?.Failed > 0 ? ExitCodes.ERROR_WRONG_INPUT : ExitCodes.SUCCESS;
    }
}
