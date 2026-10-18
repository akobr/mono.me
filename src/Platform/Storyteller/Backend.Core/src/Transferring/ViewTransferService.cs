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

    // The passes of an import (1.15): the files of the included kinds are read, the annotations are imported (ancestors
    // first), the overlay is filled, and the schemas, templates and configurations follow. A file that failed while
    // reading is reported in the pass of its kind.
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

        var progress = new ImportProgress(include, content.Files.Count, content.IgnoredFiles, content.ExcludedFiles, _options.MaxReportedItems);
        var current = await SaveImportAsync(record, progress.Apply(import), cancellationToken);
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

        var files = OrderByPass(content.Files);
        var stored = await ReadStoredAsync(record, files, options, cancellationToken);
        var overlay = new ViewImportOverlay();
        var isOverlayFilled = false;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // after the annotation pass, the keys that the import makes active are known
            if (!isOverlayFilled && file.Kind != ViewDocumentKind.Annotation)
            {
                FillOverlay(files, stored, options, overlay);
                isOverlayFilled = true;
            }

            var result = await ImportFileOrConflictAsync(record, file, options, overlay);
            Track(file, result, overlay);
            progress.Add(file, Truncate(result));

            var now = _time.GetUtcNow();

            if (progress.Processed % ProgressEveryFiles == 0 || now - savedAt >= ProgressInterval)
            {
                current = await SaveImportAsync(current, progress.Apply(current.Import!), cancellationToken);
                savedAt = now;

                if (current is null)
                {
                    return;
                }
            }
        }

        var completedAt = _time.GetUtcNow();
        var completed = progress.Apply(current.Import!) with
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

    // What the overlay needs to know of the view before the annotation pass (1.10), with at most one read per kind:
    // which annotations of the schema and configuration files are active, and, with Skip, which schemas and
    // configurations the view already has. Templates are not in the overlay, and an annotation import reads its key.
    private async Task<StoredDocuments> ReadStoredAsync(
        ViewTransferRecord record,
        IReadOnlyList<ViewArchiveFile> files,
        ViewImportOptions options,
        CancellationToken cancellationToken)
    {
        var (organization, project, view) = (record.Organization, record.Project, record.View);
        var candidates = files.Where(IsOverlayCandidate).ToList();
        var keys = candidates
            .Where(file => file.Kind != ViewDocumentKind.TypeSchema)
            .Select(file => file.AnnotationKey!)
            .DistinctBy(key => key.ToString(), StringComparer.Ordinal)
            .ToList();

        var annotations = keys.Count > 0
            ? await _annotations.FindActiveAnnotationKeysAsync(organization, project, view, keys, cancellationToken)
            : new HashSet<string>();

        var isSkip = options.Existing == ViewImportExisting.Skip;
        var schemas = isSkip && candidates.Any(file => file.Kind != ViewDocumentKind.Configuration)
            ? await _schemas.GetSchemaKeysAsync(organization, project, view, cancellationToken)
            : new HashSet<SchemaKey>();
        var configurations = isSkip && candidates.Any(file => file.Kind == ViewDocumentKind.Configuration)
            ? await ReadConfigurationKeysAsync(organization, project, view, cancellationToken)
            : new HashSet<string>();

        return new StoredDocuments(annotations, schemas, configurations);
    }

    // The annotations of the view whose own document is not empty, through the pages of the configuration listing.
    private async Task<IReadOnlySet<string>> ReadConfigurationKeysAsync(string organization, string project, string view, CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string? continuationToken = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await _configurations.ListConfigurationsAsync(organization, project, view, continuationToken: continuationToken);
            keys.UnionWith(page.Configurations.Where(configuration => configuration.HasContent).Select(configuration => configuration.AnnotationKey));
            continuationToken = page.ContinuationToken;
        }
        while (!string.IsNullOrEmpty(continuationToken));

        return keys;
    }

    // A write that keeps hitting concurrent changes after its own retries fails its file, not the whole job: the
    // overlay then treats the file as failed, so the documents that depend on it are skipped with DependencyFailed.
    private async Task<ViewImportResult> ImportFileOrConflictAsync(ViewTransferRecord record, ViewArchiveFile file, ViewImportOptions options, ViewImportOverlay overlay)
    {
        try
        {
            return await ImportFileAsync(record, file, options, overlay);
        }
        catch (Exception exception) when (IsWriteConflict(exception))
        {
            _logger.LogWarning(exception, "The file {Path} of the import {Id} failed on concurrent changes.", file.Path, record.Id);
            return new ViewImportResult(
                ViewImportOutcome.Failed,
                ErrorCode: ErrorCodes.Conflict,
                Message: $"The document was changed concurrently while it was imported; import the file again. {exception.Message}");
        }
    }

    private static bool IsWriteConflict(Exception exception)
    {
        return exception is SchemaConcurrencyException or TemplateConcurrencyException
            || (exception is ConflictException conflict && conflict.ErrorCode == ErrorCodes.Conflict);
    }

    private Task<ViewImportResult> ImportFileAsync(ViewTransferRecord record, ViewArchiveFile file, ViewImportOptions options, ViewImportOverlay overlay)
    {
        if (file.ErrorCode is not null)
        {
            return Task.FromResult(new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: file.ErrorCode, Message: file.Message));
        }

        var (organization, project, view) = (record.Organization, record.Project, record.View);

        return file.Kind switch
        {
            ViewDocumentKind.Annotation => _annotations.ImportAnnotationAsync(
                FullKey.Create(file.AnnotationKey!, organization, project, view),
                file.Content!,
                options,
                overlay),
            ViewDocumentKind.Template => _templates.ImportTemplateAsync(organization, project, view, file.AnnotationType!, file.Content!, options),
            ViewDocumentKind.Configuration => _configurations.ImportConfigurationAsync(
                FullKey.Create(file.AnnotationKey!, organization, project, view),
                file.Content!,
                options,
                overlay),
            _ => _schemas.ImportSchemaAsync(
                new ViewSchemaTarget(organization, project, view, file.Kind, file.AnnotationType, file.AnnotationKey?.ToString()),
                file.Content!,
                options,
                overlay),
        };
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

    // The files in the passes of an import (Order): annotations by depth in the graph, type and key, so ancestors come
    // first; type, descendant-type and annotation schemas, each by type and key; templates by type; configurations by
    // key. A duplicate follows the file that holds its document (the first path).
    private static List<ViewArchiveFile> OrderByPass(IReadOnlyList<ViewArchiveFile> files)
    {
        return files
            .OrderBy(file => ViewArchiveNames.GetContent(file.Kind))
            .ThenBy(file => file.Kind)
            .ThenBy(file => file.Kind == ViewDocumentKind.Annotation ? file.AnnotationKey!.GetDepth() : 0)
            .ThenBy(file => file.Kind == ViewDocumentKind.Annotation ? file.AnnotationKey!.Type : default)
            .ThenBy(file => file.AnnotationType, StringComparer.Ordinal)
            .ThenBy(file => file.AnnotationKey?.ToString(), StringComparer.Ordinal)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .ToList();
    }

    // A schema or configuration file that the import may write: read without an error, and not empty.
    private static bool IsOverlayCandidate(ViewArchiveFile file)
    {
        return file.ErrorCode is null
               && file.Content is { HasValues: true }
               && file.Kind is not (ViewDocumentKind.Annotation or ViewDocumentKind.Template);
    }

    // The schemas and configurations of the archive that the import will write (1.10), once the annotation pass is
    // done: not empty, of an annotation that is active (stored, or made active by the import), and allowed by
    // Existing. With Skip, a document the view already has stays, except the configuration of a re-created
    // annotation, which the re-create emptied. Whether a schema is a valid JSON Schema is left to the schema service,
    // which ignores the invalid layers of the overlay.
    private static void FillOverlay(IEnumerable<ViewArchiveFile> files, StoredDocuments stored, ViewImportOptions options, ViewImportOverlay overlay)
    {
        var isSkip = options.Existing == ViewImportExisting.Skip;

        foreach (var file in files.Where(IsOverlayCandidate))
        {
            var key = file.AnnotationKey?.ToString();
            var hasAnnotation = key is not null && (stored.ActiveAnnotations.Contains(key) || overlay.ActiveAnnotations.Contains(key));

            if (file.Kind == ViewDocumentKind.Configuration)
            {
                if (hasAnnotation
                    && (!isSkip || !stored.Configurations.Contains(key!) || overlay.RecreatedAnnotations.Contains(key!)))
                {
                    overlay.SetConfiguration(file.AnnotationKey!, file.Content!);
                }

                continue;
            }

            var schemaKey = GetSchemaKey(file);

            if ((file.Kind == ViewDocumentKind.TypeSchema || hasAnnotation)
                && (!isSkip || !stored.Schemas.Contains(schemaKey)))
            {
                overlay.SetSchema(schemaKey, file.Content!);
            }
        }
    }

    // The overlay follows the outcomes. An annotation that fails, or is skipped for a failed ancestor, fails what hangs
    // on it. A schema or configuration that fails or is skipped is not written, so it leaves the overlay. A duplicate
    // file changes nothing: its document is the one of the first file.
    private static void Track(ViewArchiveFile file, ViewImportResult result, ViewImportOverlay overlay)
    {
        if (file.ErrorCode == ErrorCodes.DuplicateFile)
        {
            return;
        }

        switch (file.Kind)
        {
            case ViewDocumentKind.Annotation:
                if (result.Outcome == ViewImportOutcome.Failed || result.ErrorCode == ErrorCodes.DependencyFailed)
                {
                    overlay.AddFailedAnnotation(file.AnnotationKey!);
                }

                break;

            case ViewDocumentKind.Template:
                break;

            case ViewDocumentKind.Configuration:
                if (result.Outcome is ViewImportOutcome.Failed or ViewImportOutcome.Skipped)
                {
                    overlay.RemoveConfiguration(file.AnnotationKey!);
                }

                break;

            default:
                if (result.Outcome is ViewImportOutcome.Failed or ViewImportOutcome.Skipped)
                {
                    overlay.RemoveSchema(GetSchemaKey(file));
                }

                break;
        }
    }

    private static SchemaKey GetSchemaKey(ViewArchiveFile file)
    {
        return file.Kind switch
        {
            ViewDocumentKind.TypeSchema => SchemaKey.ForType(file.AnnotationType!),
            ViewDocumentKind.AnnotationSchema => SchemaKey.ForAnnotation(file.AnnotationKey!.ToString()),
            _ => SchemaKey.ForDescendantType(file.AnnotationKey!.ToString(), file.AnnotationType!),
        };
    }

    // The stored documents an import needs to know of before its annotation pass, by canonical keys.
    private sealed record class StoredDocuments(
        IReadOnlySet<string> ActiveAnnotations,
        IReadOnlySet<SchemaKey> Schemas,
        IReadOnlySet<string> Configurations);

    // Counts of an import while it runs, in total and per included kind; the reported items stop at the limit.
    private sealed class ImportProgress(IReadOnlyList<ViewArchiveContent> include, int total, int ignoredFiles, int excludedFiles, int maxItems)
    {
        private readonly List<ViewImportItem> _items = [];
        private readonly Counts _total = new();
        private readonly Dictionary<ViewArchiveContent, Counts> _counts = include.ToDictionary(content => content, _ => new Counts());
        private bool _truncated;

        public int Processed { get; private set; }

        public void Add(ViewArchiveFile file, ViewImportResult result)
        {
            Processed++;
            _total.Add(result);

            // the reader returns the included kinds only
            if (_counts.TryGetValue(ViewArchiveNames.GetContent(file.Kind), out var counts))
            {
                counts.Add(result);
            }

            if (result.Outcome == ViewImportOutcome.Unchanged)
            {
                return;
            }

            if (_items.Count >= maxItems)
            {
                _truncated = true;
                return;
            }

            _items.Add(new ViewImportItem
            {
                Path = file.Path,
                Kind = file.Kind,
                AnnotationType = file.AnnotationType,
                AnnotationKey = file.AnnotationKey?.ToString(),
                Outcome = result.Outcome,
                Version = result.Version,
                ErrorCode = result.ErrorCode,
                Message = result.Message,
                Warnings = result.Warnings,
            });
        }

        public ViewImport Apply(ViewImport import)
        {
            return import with
            {
                TotalCount = total,
                ProcessedCount = Processed,
                Summary = _total.ToSummary(null) with { ExcludedFiles = excludedFiles, IgnoredFiles = ignoredFiles },
                Summaries = include.Select(content => _counts[content].ToSummary(content)).ToList(),
                Items = _items.ToList(),
                AreItemsTruncated = _truncated,
            };
        }

        private sealed class Counts
        {
            private int _created;
            private int _updated;
            private int _unchanged;
            private int _skipped;
            private int _failed;
            private int _warnings;

            public void Add(ViewImportResult result)
            {
                switch (result.Outcome)
                {
                    case ViewImportOutcome.Created:
                        _created++;
                        break;

                    case ViewImportOutcome.Updated:
                        _updated++;
                        break;

                    case ViewImportOutcome.Unchanged:
                        _unchanged++;
                        return;

                    case ViewImportOutcome.Skipped:
                        _skipped++;
                        break;

                    default:
                        _failed++;
                        break;
                }

                if (result.Warnings is { Count: > 0 })
                {
                    _warnings++;
                }
            }

            public ViewImportSummary ToSummary(ViewArchiveContent? content)
            {
                return new ViewImportSummary
                {
                    Content = content,
                    Created = _created,
                    Updated = _updated,
                    Unchanged = _unchanged,
                    Skipped = _skipped,
                    Failed = _failed,
                    Warnings = _warnings,
                };
            }
        }
    }
}
