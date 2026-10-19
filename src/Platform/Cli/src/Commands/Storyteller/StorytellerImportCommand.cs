using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;

namespace _42.Platform.Cli.Commands.Storyteller;

[Command(CommandNames.IMPORT, Description = "Import a zip or a folder into the view (Administrator): annotations, schemas, templates and configurations, each recognized by its file name. Only current documents are imported, never versions, and nothing is deleted. A machine needs the scopes of the included kinds instead of a role.")]
public class StorytellerImportCommand : BaseContextCommand
{
    private readonly ITransfersApiClient _transfersApi;
    private readonly IFileSystem _fileSystem;
    private readonly TransferPolling _polling;

    public StorytellerImportCommand(
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

    [Argument(0, Name = "zip-or-folder", Description = "A zip, for example an export, or a folder; its *.json files are packed, without hidden folders and files (.git, .github, ._*).")]
    public string Path { get; set; } = string.Empty;

    [Option("-i|--include", CommandOptionType.MultipleValue, ValueName = "kinds", Description = "The kinds to import: annotations, schemas, templates and configurations, comma-separated or repeated; all four by default. Files of other kinds are excluded.")]
    public string[]? Include { get; set; }

    [Option("-e|--existing", CommandOptionType.SingleValue, ValueName = "mode", Description = "What happens to a document the view already has: 'overwrite' (default) writes the file as a new version or an update; 'skip' keeps it.")]
    public string? Existing { get; set; }

    [Option("--dry-run", CommandOptionType.NoValue, Description = "Validate and compare only, and report every outcome and warning; nothing is written.")]
    public bool IsDryRun { get; set; }

    [Option("--force", CommandOptionType.NoValue, Description = "Store schemas and configurations that break each other, with a warning; without it, they fail and the rest is imported.")]
    public bool IsForced { get; set; }

    [Option("--timeout", CommandOptionType.SingleValue, ValueName = "minutes", Description = "Minutes to wait for the import; the import continues on the server after that.")]
    public int TimeoutMinutes { get; set; } = 10;

    protected override async Task<int> ExecuteAsync()
    {
        if (!ViewTransferArguments.TryParseInclude(Include, out var include, out var error)
            || !ViewTransferArguments.TryParseExisting(Existing, out var existing, out error))
        {
            Console.WriteLine(error.ThemedError(Console.Theme));
            return ExitCodes.ERROR_WRONG_INPUT;
        }

        Stream archive;
        string fileName;

        if (!string.IsNullOrWhiteSpace(Path) && _fileSystem.Directory.Exists(Path))
        {
            var (packed, count) = ViewArchives.PackFolder(_fileSystem, Path);

            if (count == 0)
            {
                packed.Dispose();
                Console.WriteLine($"The folder '{Path}' has no *.json files outside hidden folders.".ThemedError(Console.Theme));
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

        ViewImport import;

        await using (archive)
        {
            try
            {
                import = await _transfersApi.StartViewImportAsync(
                    Context.OrganizationName,
                    Context.ProjectName,
                    Context.ViewName,
                    include,
                    existing,
                    IsForced ? true : null,
                    IsDryRun ? true : null,
                    fileName,
                    archive);
            }
            catch (ApiException exception) when (exception.StatusCode is 400 or 403 or 409 or 413)
            {
                // a 401 goes to Program.Main, at every step
                return Console.WriteRefusal(exception);
            }
        }

        Console.WriteLine(
            $"Importing {fileName} into {Context.OrganizationName}.{Context.ProjectName}.{Context.ViewName}{FormatOptions(include, existing)}…"
                .ThemedLowlight(Console.Theme));

        ViewImport finished;
        bool isDone;

        try
        {
            (finished, isDone) = await _polling.WaitAsync(
                () => _transfersApi.GetViewImportAsync(Context.OrganizationName, Context.ProjectName, Context.ViewName, import.Id),
                job => job.Status is ViewImportStatus.Succeeded or ViewImportStatus.Failed,
                TimeSpan.FromMinutes(TimeoutMinutes));
        }
        catch (ApiException exception) when (exception.StatusCode is 403)
        {
            // every read checks the current role, which can be lowered while the job runs
            return Console.WriteRefusal(exception);
        }

        if (!isDone)
        {
            Console.WriteLine($"The import {import.Id} is still {finished.Status.ToString().ToLowerInvariant()} ({finished.ProcessedCount ?? 0} of {finished.TotalCount ?? 0} files); it continues on the server.");
            return ExitCodes.WARNING_ABORTED;
        }

        if (finished.Status == ViewImportStatus.Failed)
        {
            // the server writes the whole message, prefix included
            Console.WriteLine((finished.Error ?? "The import failed.").ThemedError(Console.Theme));
            return ExitCodes.ERROR_CRASH;
        }

        Console.WriteHeader($"Import of {fileName}");
        Console.WriteImport(finished);

        // warnings do not change the exit code
        return finished.Summary?.Failed > 0 ? ExitCodes.ERROR_WRONG_INPUT : ExitCodes.SUCCESS;
    }

    // " (annotations and templates, skip existing, forced, dry run)", or nothing for the defaults.
    private string FormatOptions(IReadOnlyList<ViewArchiveContent>? include, ViewImportExisting? existing)
    {
        var options = new List<string>();

        if (include is not null)
        {
            options.Add(ViewTransferArguments.FormatKinds(include));
        }

        if (existing == ViewImportExisting.Skip)
        {
            options.Add("skip existing");
        }

        if (IsForced)
        {
            options.Add("forced");
        }

        if (IsDryRun)
        {
            options.Add("dry run");
        }

        return options.Count == 0 ? string.Empty : $" ({string.Join(", ", options)})";
    }
}
