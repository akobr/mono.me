using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Configuration;

[Command(CommandNames.EXPORT, Description = "Export the configurations of the view as a zip: a folder of JSON files with their versions, or a git repository with every version as a commit.")]
public class ConfigExportCommand : BaseContextCommand
{
    private const string FolderFormat = "folder";
    private const string GitFormat = "git";
    private const string ZipExtension = ".zip";

    private readonly IConfigurationsApiClient _configurationApi;
    private readonly IFileSystem _fileSystem;
    private readonly TransferPolling _polling;

    public ConfigExportCommand(
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

    [Option("-f|--format", CommandOptionType.SingleValue, Description = "The format of the archive: 'folder' (default) or 'git'.")]
    public string Format { get; set; } = FolderFormat;

    [Option("-o|--output", CommandOptionType.SingleValue, Description = "The file of the zip, or a folder for it; the server's file name in the current folder by default. With --extract, the folder to unpack into.")]
    public string? Output { get; set; }

    [Option("-x|--extract", CommandOptionType.NoValue, Description = "Unpack the archive into a new or empty folder instead of saving the zip.")]
    public bool IsExtractRequested { get; set; }

    [Option("--timeout", CommandOptionType.SingleValue, Description = "Minutes to wait for the export (10 by default); the export continues on the server after that.")]
    public int TimeoutMinutes { get; set; } = 10;

    protected override async Task<int> ExecuteAsync()
    {
        ConfigurationExportCreateFormat format;

        if (Format.Equals(FolderFormat, StringComparison.OrdinalIgnoreCase))
        {
            format = ConfigurationExportCreateFormat.Folder;
        }
        else if (Format.Equals(GitFormat, StringComparison.OrdinalIgnoreCase))
        {
            format = ConfigurationExportCreateFormat.Git;
        }
        else
        {
            Console.WriteLine($"Unknown format '{Format}'; use 'folder' or 'git'.".ThemedError(Console.Theme));
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var target = $"{Context.OrganizationName}.{Context.ProjectName}.{Context.ViewName}";
        ConfigurationExport export;

        try
        {
            export = await _configurationApi.StartConfigurationExportAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                new ConfigurationExportCreate { Format = format });
        }
        catch (ApiException exception) when (exception.StatusCode == 409)
        {
            Console.WriteLine(exception.GetErrorMessage().ThemedError(Console.Theme));
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Console.WriteLine($"Exporting the configurations of {target} ({Format.ToLowerInvariant()})…".ThemedLowlight(Console.Theme));

        var (finished, isDone) = await _polling.WaitAsync(
            () => _configurationApi.GetConfigurationExportAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, export.Id),
            job => job.Status is ConfigurationExportStatus.Succeeded or ConfigurationExportStatus.Failed,
            TimeSpan.FromMinutes(TimeoutMinutes));

        if (!isDone)
        {
            Console.WriteLine($"The export {export.Id} is still {finished.Status.ToString().ToLowerInvariant()}; it continues on the server.");
            return ExitCodes.WARNING_ABORTED;
        }

        if (finished.Status == ConfigurationExportStatus.Failed)
        {
            // the server writes the whole message, prefix included
            Console.WriteLine((finished.Error ?? "The export failed.").ThemedError(Console.Theme));
            return ExitCodes.ERROR_CRASH;
        }

        using var archive = await _configurationApi.GetConfigurationExportArchiveAsync(
            Context.OrganizationName,
            Context.ProjectName,
            Context.ViewName,
            export.Id);

        string destination;

        if (IsExtractRequested)
        {
            destination = Output ?? StripZip(finished.FileName);

            try
            {
                await ConfigurationArchives.ExtractAsync(_fileSystem, archive.Stream, destination);
            }
            catch (InvalidOperationException exception)
            {
                Console.WriteLine(exception.Message.ThemedError(Console.Theme));
                return ExitCodes.ERROR_WRONG_INPUT;
            }
        }
        else
        {
            destination = ResolveOutputFile(finished.FileName);
            var folder = _fileSystem.Path.GetDirectoryName(_fileSystem.Path.GetFullPath(destination));

            if (!string.IsNullOrEmpty(folder))
            {
                _fileSystem.Directory.CreateDirectory(folder);
            }

            await using var file = _fileSystem.File.Create(destination);
            await archive.Stream.CopyToAsync(file);
        }

        Console.WriteImportant(
            $"Exported {TransferConsoleExtensions.Count(finished.ConfigurationCount, "configuration")} with "
            + $"{TransferConsoleExtensions.Count(finished.VersionCount, "version")} ({TransferConsoleExtensions.FormatSize(finished.Size)}) "
            + $"{(IsExtractRequested ? "into" : "to")} {destination}.");
        return ExitCodes.SUCCESS;
    }

    // A folder (existing, or written with a trailing separator) gets the server's file name; anything else is the file.
    private string ResolveOutputFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(Output))
        {
            return fileName;
        }

        var isFolder = _fileSystem.Directory.Exists(Output)
                       || Output.EndsWith(_fileSystem.Path.DirectorySeparatorChar)
                       || Output.EndsWith(_fileSystem.Path.AltDirectorySeparatorChar);

        return isFolder ? _fileSystem.Path.Combine(Output, fileName) : Output;
    }

    private static string StripZip(string fileName)
    {
        return fileName.EndsWith(ZipExtension, StringComparison.OrdinalIgnoreCase) ? fileName[..^ZipExtension.Length] : fileName;
    }
}
