using System;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.EXPORT, Description = "Export the view as a zip (Administrator): its annotations, schemas, templates and configurations, as a folder of JSON files with the versions, or as a git repository with every version as a commit. A machine needs the scopes of the included kinds instead of a role.")]
public class StorytellerExportCommand : BaseContextCommand
{
    private const string ZipExtension = ".zip";

    private readonly ITransfersApiClient _transfersApi;
    private readonly IFileSystem _fileSystem;
    private readonly TransferPolling _polling;

    public StorytellerExportCommand(
        IExtendedConsole console,
        ICommandContext context,
        ITransfersApiClient transfersApi,
        IFileSystem fileSystem,
        TransferPolling polling)
        : base(console, context)
    {
        _transfersApi = transfersApi;
        _fileSystem = fileSystem;
        _polling = polling;
    }

    [Option("-f|--format", CommandOptionType.SingleValue, ValueName = "format", Description = "The format of the archive: 'folder' or 'git'.")]
    public string Format { get; set; } = ViewTransferArguments.FolderFormat;

    [Option("-i|--include", CommandOptionType.MultipleValue, ValueName = "kinds", Description = "The kinds to export: annotations, schemas, templates and configurations, comma-separated or repeated; all four by default.")]
    public string[]? Include { get; set; }

    [Option("-o|--output", CommandOptionType.SingleValue, ValueName = "path", Description = "The file of the zip, or a folder for it; the server's file name in the current folder by default. With --extract, the folder to unpack into.")]
    public string? Output { get; set; }

    [Option("-x|--extract", CommandOptionType.NoValue, Description = "Unpack the archive into a new or empty folder instead of saving the zip.")]
    public bool IsExtractRequested { get; set; }

    [Option("--timeout", CommandOptionType.SingleValue, ValueName = "minutes", Description = "Minutes to wait for the export; the export continues on the server after that.")]
    public int TimeoutMinutes { get; set; } = 10;

    protected override async Task<int> ExecuteAsync()
    {
        if (!ViewTransferArguments.TryParseFormat(Format, out var format, out var error)
            || !ViewTransferArguments.TryParseInclude(Include, out var include, out error))
        {
            Console.WriteLine(error.ThemedError(Console.Theme));
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        var target = $"{Context.OrganizationName}.{Context.ProjectName}.{Context.ViewName}";
        ViewExport export;

        try
        {
            // always a body: the API answers 400 to the null that the SDK sends for a missing one
            export = await _transfersApi.StartViewExportAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                new ViewExportCreate { Format = format, Include = include is null ? null : [.. include] });
        }
        catch (ApiException exception) when (exception.StatusCode is 400 or 403 or 409)
        {
            // a 401 goes to Program.Main, at every step
            return Console.WriteRefusal(exception);
        }

        Console.WriteLine(
            $"Exporting the {ViewTransferArguments.FormatKinds(include)} of {target} ({format.ToString().ToLowerInvariant()})…"
                .ThemedLowlight(Console.Theme));

        ViewExport finished;
        bool isDone;

        try
        {
            (finished, isDone) = await _polling.WaitAsync(
                () => _transfersApi.GetViewExportAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, export.Id),
                job => job.Status is ViewExportStatus.Succeeded or ViewExportStatus.Failed,
                TimeSpan.FromMinutes(TimeoutMinutes));
        }
        catch (ApiException exception) when (exception.StatusCode is 403)
        {
            // every read checks the current role, which can be lowered while the job runs
            return Console.WriteRefusal(exception);
        }

        if (!isDone)
        {
            Console.WriteLine($"The export {export.Id} is still {finished.Status.ToString().ToLowerInvariant()}; it continues on the server.");
            return ExitCodes.WARNING_ABORTED;
        }

        if (finished.Status == ViewExportStatus.Failed)
        {
            // the server writes the whole message, prefix included
            Console.WriteLine((finished.Error ?? "The export failed.").ThemedError(Console.Theme));
            return ExitCodes.ERROR_CRASH;
        }

        FileResponse archive;

        try
        {
            archive = await _transfersApi.GetViewExportArchiveAsync(
                Context.OrganizationName,
                Context.ProjectName,
                Context.ViewName,
                export.Id);
        }
        catch (ApiException exception) when (exception.StatusCode is 403)
        {
            // nothing is written: the file and its folder are created only for a served archive
            return Console.WriteRefusal(exception);
        }

        string destination;

        using (archive)
        {
            if (IsExtractRequested)
            {
                destination = Output ?? StripZip(finished.FileName);

                try
                {
                    await ViewArchives.ExtractAsync(_fileSystem, archive.Stream, destination);
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
        }

        Console.WriteImportant(
            $"Exported {TransferConsoleExtensions.FormatExportSummary(finished)} {(IsExtractRequested ? "into" : "to")} {destination}.");
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
