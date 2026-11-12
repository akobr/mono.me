using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Transferring;

public sealed class ViewTransferService : IViewTransferService
{
    private const int ProgressEveryFiles = 50;
    private const int MaxMessageLength = 500;
    private const int MaxFileNameLength = 255;

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(2);

    // A queued job unchanged for this long counts as abandoned, so a lost message cannot block the project's limit.
    private static readonly TimeSpan QueuedWindow = TimeSpan.FromHours(1);

    // A running job saves its progress every few seconds, so one unchanged for this long has lost its worker.
    private static readonly TimeSpan RunningWindow = TimeSpan.FromMinutes(15);

    // Archives are purged a little after their jobs expire.
    private static readonly TimeSpan PurgeDelay = TimeSpan.FromHours(1);

    private readonly IViewTransferStore _store;
    private readonly IViewArchiveStorage _storage;
    private readonly IViewTransferQueue _queue;
    private readonly IAnnotationService _annotations;
    private readonly IConfigurationSchemaService _schemas;
    private readonly IConfigurationTemplateService _templates;
    private readonly IConfigurationService _configurations;
    private readonly ViewImporter _importer;
    private readonly ViewTransferOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ViewTransferService> _logger;

    public ViewTransferService(
        IViewTransferStore store,
        IViewArchiveStorage storage,
        IViewTransferQueue queue,
        IAnnotationService annotations,
        IConfigurationSchemaService schemas,
        IConfigurationTemplateService templates,
        IConfigurationService configurations,
        IOptions<ViewTransferOptions> options,
        TimeProvider time,
        ILogger<ViewTransferService> logger)
    {
        _store = store;
        _storage = storage;
        _queue = queue;
        _annotations = annotations;
        _schemas = schemas;
        _templates = templates;
        _configurations = configurations;
        _options = options.Value;
        _time = time;
        _logger = logger;

        // built from the same services rather than injected, so the constructor and its callers stay as they were
        _importer = new ViewImporter(annotations, schemas, templates, configurations, logger);
    }

    public async Task<ViewExport> StartExportAsync(
        string organization,
        string project,
        string view,
        ViewArchiveFormat format,
        IReadOnlyList<ViewArchiveContent>? include,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        await EnsureCapacityAsync(organization, project, cancellationToken);

        var now = _time.GetUtcNow();
        var id = NewId();
        var record = await _store.CreateAsync(
            new ViewTransferRecord
            {
                Organization = organization,
                Project = project,
                View = view,
                Kind = ViewTransferKind.Export,
                Id = id,
                Export = new ViewExport
                {
                    Id = id,
                    Status = ViewTransferStatus.Queued,
                    Format = format,
                    Include = ViewArchiveInclude.Resolve(include),
                    FileName = ViewArchiveNames.GetArchiveFileName(organization, project, view, format, now),
                    CreatedAt = now,
                    CreatedBy = actor,
                    ExpiresAt = now + _options.Retention,
                },
            },
            cancellationToken);

        await EnqueueAsync(record, cancellationToken);
        return record.Export!;
    }

    public async Task<ViewExport?> GetExportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(organization, project, view, ViewTransferKind.Export, id, cancellationToken);
        return record?.Export;
    }

    public async Task<(ViewExport Export, Stream Archive)> OpenExportArchiveAsync(
        string organization,
        string project,
        string view,
        string id,
        CancellationToken cancellationToken = default)
    {
        var export = await GetExportAsync(organization, project, view, id, cancellationToken)
                     ?? throw new NotFoundException($"The export '{id}' does not exist or has expired.");

        if (export.Status != ViewTransferStatus.Succeeded)
        {
            throw new ConflictException(
                export.Status == ViewTransferStatus.Failed
                    ? $"The export '{id}' failed: {export.Error}"
                    : $"The export '{id}' is not ready yet ({export.Status}).",
                ErrorCodes.TransferNotReady);
        }

        var name = ViewTransferNames.GetArchiveName(organization, project, view, ViewTransferKind.Export, id);
        var archive = await _storage.OpenReadAsync(name, cancellationToken)
                      ?? throw new NotFoundException($"The archive of the export '{id}' has expired.");
        return (export, archive);
    }

    public async Task<ViewImport> StartImportAsync(
        string organization,
        string project,
        string view,
        Stream archive,
        string? fileName,
        IReadOnlyList<ViewArchiveContent>? include,
        ViewImportExisting existing,
        bool force,
        bool dryRun,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        if (!archive.CanSeek)
        {
            throw new ArgumentException("The archive must be a seekable stream.", nameof(archive));
        }

        await EnsureCapacityAsync(organization, project, cancellationToken);

        var start = archive.Position;
        ViewArchiveReader.Validate(archive, _options);
        archive.Position = start;

        var now = _time.GetUtcNow();
        var id = NewId();
        await _storage.UploadAsync(
            ViewTransferNames.GetArchiveName(organization, project, view, ViewTransferKind.Import, id),
            archive,
            cancellationToken);

        var record = await _store.CreateAsync(
            new ViewTransferRecord
            {
                Organization = organization,
                Project = project,
                View = view,
                Kind = ViewTransferKind.Import,
                Id = id,
                Import = new ViewImport
                {
                    Id = id,
                    Status = ViewTransferStatus.Queued,
                    FileName = CleanFileName(fileName),
                    Include = ViewArchiveInclude.Resolve(include),
                    Existing = existing,
                    IsDryRun = dryRun,
                    IsForced = force,
                    CreatedAt = now,
                    CreatedBy = actor,
                    ExpiresAt = now + _options.Retention,
                },
            },
            cancellationToken);

        await EnqueueAsync(record, cancellationToken);
        return record.Import!;
    }

    public async Task<ViewImport?> GetImportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(organization, project, view, ViewTransferKind.Import, id, cancellationToken);
        return record?.Import;
    }

    public async Task ProcessAsync(ViewTransferMessage message, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(message.Organization, message.Project, message.View, message.Kind, message.Id, cancellationToken);

        if (record is null)
        {
            _logger.LogWarning("The {Kind} job {Id} of {Organization}.{Project}.{View} no longer exists.", message.Kind, message.Id, message.Organization, message.Project, message.View);
            return;
        }

        if (record.Status is ViewTransferStatus.Succeeded or ViewTransferStatus.Failed)
        {
            _logger.LogInformation("The {Kind} job {Id} is already {Status}.", message.Kind, message.Id, record.Status);
            return;
        }

        var now = _time.GetUtcNow();
        var running = await _store.TryReplaceAsync(
            Update(
                record,
                export => export with { Status = ViewTransferStatus.Running, StartedAt = now, ExpiresAt = now + _options.Retention },
                import => import with { Status = ViewTransferStatus.Running, StartedAt = now, ExpiresAt = now + _options.Retention }),
            cancellationToken);

        if (running is null)
        {
            _logger.LogInformation("The {Kind} job {Id} was taken by another worker.", message.Kind, message.Id);
            return;
        }

        try
        {
            if (running.Kind == ViewTransferKind.Export)
            {
                await ExportAsync(running, cancellationToken);
            }
            else
            {
                await ImportAsync(running, cancellationToken);
            }
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "The {Kind} job {Id} of {Organization}.{Project}.{View} failed.", message.Kind, message.Id, message.Organization, message.Project, message.View);
            await FailAsync(message, exception is StorytellerException ? exception.Message : $"The {message.Kind.ToString().ToLowerInvariant()} failed: {exception.Message}", cancellationToken);

            if (message.Kind == ViewTransferKind.Export)
            {
                await TryDeleteArchiveAsync(ViewTransferNames.GetArchiveName(message), cancellationToken);
            }
        }
    }

    public async Task AbandonAsync(ViewTransferMessage message, CancellationToken cancellationToken = default)
    {
        _logger.LogError("The queue gave up on the {Kind} job {Id} of {Organization}.{Project}.{View}.", message.Kind, message.Id, message.Organization, message.Project, message.View);
        var failed = await FailAsync(
            message,
            $"The {message.Kind.ToString().ToLowerInvariant()} stopped on every attempt, for example because its worker ran out of memory; try again with a smaller archive or fewer kinds.",
            cancellationToken);

        // the archive of an export that ended before stays downloadable
        if (failed && message.Kind == ViewTransferKind.Export)
        {
            await TryDeleteArchiveAsync(ViewTransferNames.GetArchiveName(message), cancellationToken);
        }
    }

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var threshold = _time.GetUtcNow() - _options.Retention - PurgeDelay;
        return _storage.DeleteOlderThanAsync(threshold, cancellationToken);
    }

    private async Task ExportAsync(ViewTransferRecord record, CancellationToken cancellationToken)
    {
        var export = record.Export!;
        var include = ViewArchiveInclude.Resolve(export.Include);
        var (organization, project, view) = (record.Organization, record.Project, record.View);

        // The reads are not a snapshot, so they run in reverse dependency order: configurations, templates, schemas,
        // annotations. A document read first still finds its dependency in a later read unless it was deleted in
        // between, which is rarer than a creation during the export.
        var configurations = await ReadAsync(ViewArchiveContent.Configurations, () => _configurations.GetConfigurationHistoriesAsync(organization, project, view, cancellationToken));
        var templates = await ReadAsync(ViewArchiveContent.Templates, () => _templates.GetTemplateHistoriesAsync(organization, project, view, cancellationToken));
        var schemas = await ReadAsync(ViewArchiveContent.Schemas, () => _schemas.GetSchemaHistoriesAsync(organization, project, view, cancellationToken));
        var annotations = await ReadAsync(ViewArchiveContent.Annotations, () => _annotations.GetViewAnnotationsAsync(organization, project, view, cancellationToken));

        using var buffer = new MemoryStream();
        var statistics = ViewArchiveWriter.Write(
            buffer,
            new ViewArchiveSource
            {
                Organization = organization,
                Project = project,
                View = view,
                ExportedAt = export.StartedAt ?? _time.GetUtcNow(),
                ExportedBy = export.CreatedBy,
                Include = include,
                Annotations = annotations,
                Schemas = schemas,
                Templates = templates,
                Configurations = configurations,
            },
            export.Format);

        buffer.Position = 0;
        await _storage.UploadAsync(ViewTransferNames.GetArchiveName(record.ToMessage()), buffer, cancellationToken);

        var now = _time.GetUtcNow();
        var saved = await _store.TryReplaceAsync(
            record with
            {
                Export = export with
                {
                    Status = ViewTransferStatus.Succeeded,
                    CompletedAt = now,
                    ExpiresAt = now + _options.Retention,
                    AnnotationCount = Count(ViewArchiveContent.Annotations, statistics.AnnotationCount),
                    SchemaCount = Count(ViewArchiveContent.Schemas, statistics.SchemaCount),
                    TemplateCount = Count(ViewArchiveContent.Templates, statistics.TemplateCount),
                    ConfigurationCount = Count(ViewArchiveContent.Configurations, statistics.ConfigurationCount),
                    VersionCount = statistics.VersionCount,
                    Size = buffer.Length,
                },
            },
            cancellationToken);

        if (saved is null)
        {
            _logger.LogWarning("The export {Id} was changed by another worker; its result was not saved.", record.Id);
        }

        // a kind left out reads nothing and has no count
        async Task<IReadOnlyList<T>> ReadAsync<T>(ViewArchiveContent content, Func<Task<IReadOnlyList<T>>> read)
        {
            return include.Contains(content) ? await read() : [];
        }

        int? Count(ViewArchiveContent content, int count) => include.Contains(content) ? count : null;
    }

    // The files of the included kinds are read and imported in the passes of the importer (1.15). The progress is saved
    // as the outcomes come; a worker that lost the job stops the importer with it.
    private async Task ImportAsync(ViewTransferRecord record, CancellationToken cancellationToken)
    {
        var import = record.Import!;
        var include = ViewArchiveInclude.Resolve(import.Include);
        var name = ViewTransferNames.GetArchiveName(record.ToMessage());
        ViewArchiveDocuments content;

        await using (var blob = await _storage.OpenReadAsync(name, cancellationToken)
                                ?? throw new InvalidOperationException("The uploaded archive no longer exists."))
        {
            using var buffer = new MemoryStream();
            await CopyWithLimitAsync(blob, buffer, _options.MaxArchiveBytes, cancellationToken);
            buffer.Position = 0;
            content = ViewArchiveReader.Read(buffer, _options, include);
        }

        var tally = new ViewImportTally(include, content.Files.Count, content.IgnoredFiles, content.ExcludedFiles, _options.MaxReportedItems);
        var current = await SaveImportAsync(record, Apply(import, tally), cancellationToken);
        var savedAt = _time.GetUtcNow();

        if (current is null)
        {
            return;
        }

        var options = new ViewImportOptions
        {
            Actor = import.CreatedBy,
            Existing = import.Existing,
            Force = import.IsForced,
            DryRun = import.IsDryRun,
        };

        // nothing runs before the enumeration starts, so the importer's pre-reads follow the first save
        var results = _importer.ImportAsync(record.Organization, record.Project, record.View, content.Files, options, cancellationToken: cancellationToken);

        await foreach (var (file, result) in results)
        {
            tally.Add(file, Truncate(result));

            var now = _time.GetUtcNow();

            if (tally.Processed % ProgressEveryFiles == 0 || now - savedAt >= ProgressInterval)
            {
                current = await SaveImportAsync(current, Apply(current.Import!, tally), cancellationToken);
                savedAt = now;

                if (current is null)
                {
                    return;
                }
            }
        }

        var completedAt = _time.GetUtcNow();
        var completed = Apply(current.Import!, tally) with
        {
            Status = ViewTransferStatus.Succeeded,
            CompletedAt = completedAt,
            ExpiresAt = completedAt + _options.Retention,
        };

        if (await SaveImportAsync(current, completed, cancellationToken) is not null)
        {
            await TryDeleteArchiveAsync(name, cancellationToken);
        }
    }

    private async Task<ViewTransferRecord?> SaveImportAsync(ViewTransferRecord record, ViewImport import, CancellationToken cancellationToken)
    {
        var saved = await _store.TryReplaceAsync(record with { Import = import }, cancellationToken);

        if (saved is null)
        {
            _logger.LogWarning("The import {Id} was changed by another worker or expired; this worker stops.", record.Id);
        }

        return saved;
    }

    // True when this call failed the job; a missing or finished job stays as it is.
    private async Task<bool> FailAsync(ViewTransferMessage message, string error, CancellationToken cancellationToken)
    {
        try
        {
            var latest = await _store.GetAsync(message.Organization, message.Project, message.View, message.Kind, message.Id, cancellationToken);

            if (latest is null || latest.Status is ViewTransferStatus.Succeeded or ViewTransferStatus.Failed)
            {
                return false;
            }

            var now = _time.GetUtcNow();
            return await _store.TryReplaceAsync(
                Update(
                    latest,
                    export => export with { Status = ViewTransferStatus.Failed, Error = Truncate(error), CompletedAt = now, ExpiresAt = now + _options.Retention },
                    import => import with { Status = ViewTransferStatus.Failed, Error = Truncate(error), CompletedAt = now, ExpiresAt = now + _options.Retention }),
                cancellationToken) is not null;
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "The failure of the {Kind} job {Id} was not saved.", message.Kind, message.Id);
            return false;
        }
    }

    private async Task EnqueueAsync(ViewTransferRecord record, CancellationToken cancellationToken)
    {
        try
        {
            await _queue.EnqueueAsync(record.ToMessage(), cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The {Kind} job {Id} was not queued.", record.Kind, record.Id);
            await FailAsync(record.ToMessage(), "The job could not be queued; try again.", CancellationToken.None);
            throw;
        }
    }

    private async Task EnsureCapacityAsync(string organization, string project, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var active = await _store.CountActiveAsync(organization, project, now - QueuedWindow, now - RunningWindow, cancellationToken);

        if (active >= _options.MaxActiveTransfers)
        {
            throw new ConflictException(
                $"The project already has {active} exports or imports in progress; wait until one of them ends.",
                ErrorCodes.TransferLimit);
        }
    }

    private async Task TryDeleteArchiveAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteAsync(name, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "The archive {Name} was not deleted; the purge removes it later.", name);
        }
    }

    private static ViewTransferRecord Update(
        ViewTransferRecord record,
        Func<ViewExport, ViewExport> export,
        Func<ViewImport, ViewImport> import)
    {
        return record.Kind == ViewTransferKind.Export
            ? record with { Export = export(record.Export!) }
            : record with { Import = import(record.Import!) };
    }

    private static async Task CopyWithLimitAsync(Stream source, Stream target, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

            if (target.Length > limit)
            {
                throw new InvalidInputException("The uploaded archive is larger than allowed.", ErrorCodes.ArchiveTooLarge);
            }
        }
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static string? CleanFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var normalized = fileName.Replace('\\', '/');
        var name = new string(normalized[(normalized.LastIndexOf('/') + 1)..].Where(character => !char.IsControl(character)).ToArray()).Trim();

        if (name.Length == 0)
        {
            return null;
        }

        return name.Length > MaxFileNameLength ? name[..MaxFileNameLength] : name;
    }

    private static string? Truncate(string? message)
    {
        return message is { Length: > MaxMessageLength } ? $"{message[..(MaxMessageLength - 1)]}…" : message;
    }

    // The message and the warnings as the job stores them.
    private static ViewImportResult Truncate(ViewImportResult result)
    {
        return result with
        {
            Message = Truncate(result.Message),
            Warnings = result.Warnings?.Select(warning => warning with { Message = Truncate(warning.Message)! }).ToList(),
        };
    }

    // The progress of the import as the job stores it.
    private static ViewImport Apply(ViewImport import, ViewImportTally tally)
    {
        return import with
        {
            TotalCount = tally.Total,
            ProcessedCount = tally.Processed,
            Summary = tally.Summary,
            Summaries = tally.Summaries,
            Items = tally.Items,
            AreItemsTruncated = tally.AreItemsTruncated,
        };
    }
}
