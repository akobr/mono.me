using _42.Platform.Storyteller.Configuring;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Transferring;

public sealed class ConfigurationTransferService : IConfigurationTransferService
{
    private const int ProgressEveryFiles = 50;
    private const int MaxMessageLength = 500;
    private const int MaxFileNameLength = 255;

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(2);

    // A job unchanged for this long counts as abandoned, so a crashed worker cannot block the project's limit.
    private static readonly TimeSpan ActiveWindow = TimeSpan.FromHours(1);

    // Archives are purged a little after their jobs expire.
    private static readonly TimeSpan PurgeDelay = TimeSpan.FromHours(1);

    private readonly IConfigurationTransferStore _store;
    private readonly IConfigurationArchiveStorage _storage;
    private readonly IConfigurationTransferQueue _queue;
    private readonly IConfigurationService _configurations;
    private readonly ConfigurationTransferOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<ConfigurationTransferService> _logger;

    public ConfigurationTransferService(
        IConfigurationTransferStore store,
        IConfigurationArchiveStorage storage,
        IConfigurationTransferQueue queue,
        IConfigurationService configurations,
        IOptions<ConfigurationTransferOptions> options,
        TimeProvider time,
        ILogger<ConfigurationTransferService> logger)
    {
        _store = store;
        _storage = storage;
        _queue = queue;
        _configurations = configurations;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task<ConfigurationExport> StartExportAsync(
        string organization,
        string project,
        string view,
        ConfigurationArchiveFormat format,
        AuditActor actor,
        CancellationToken cancellationToken = default)
    {
        await EnsureCapacityAsync(organization, project, cancellationToken);

        var now = _time.GetUtcNow();
        var id = NewId();
        var record = await _store.CreateAsync(
            new ConfigurationTransferRecord
            {
                Organization = organization,
                Project = project,
                View = view,
                Kind = ConfigurationTransferKind.Export,
                Id = id,
                Export = new ConfigurationExport
                {
                    Id = id,
                    Status = ConfigurationTransferStatus.Queued,
                    Format = format,
                    FileName = ConfigurationFileNames.GetArchiveFileName(organization, project, view, format, now),
                    CreatedAt = now,
                    CreatedBy = actor,
                    ExpiresAt = now + _options.Retention,
                },
            },
            cancellationToken);

        await EnqueueAsync(record, cancellationToken);
        return record.Export!;
    }

    public async Task<ConfigurationExport?> GetExportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(organization, project, view, ConfigurationTransferKind.Export, id, cancellationToken);
        return record?.Export;
    }

    public async Task<(ConfigurationExport Export, Stream Archive)> OpenExportArchiveAsync(
        string organization,
        string project,
        string view,
        string id,
        CancellationToken cancellationToken = default)
    {
        var export = await GetExportAsync(organization, project, view, id, cancellationToken)
                     ?? throw new NotFoundException($"The export '{id}' does not exist or has expired.");

        if (export.Status != ConfigurationTransferStatus.Succeeded)
        {
            throw new ConflictException(
                export.Status == ConfigurationTransferStatus.Failed
                    ? $"The export '{id}' failed: {export.Error}"
                    : $"The export '{id}' is not ready yet ({export.Status}).",
                ErrorCodes.TransferNotReady);
        }

        var name = ConfigurationTransferNames.GetArchiveName(organization, project, view, ConfigurationTransferKind.Export, id);
        var archive = await _storage.OpenReadAsync(name, cancellationToken)
                      ?? throw new NotFoundException($"The archive of the export '{id}' has expired.");
        return (export, archive);
    }

    public async Task<ConfigurationImport> StartImportAsync(
        string organization,
        string project,
        string view,
        Stream archive,
        string? fileName,
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
        ConfigurationArchiveReader.Validate(archive, _options);
        archive.Position = start;

        var now = _time.GetUtcNow();
        var id = NewId();
        await _storage.UploadAsync(
            ConfigurationTransferNames.GetArchiveName(organization, project, view, ConfigurationTransferKind.Import, id),
            archive,
            cancellationToken);

        var record = await _store.CreateAsync(
            new ConfigurationTransferRecord
            {
                Organization = organization,
                Project = project,
                View = view,
                Kind = ConfigurationTransferKind.Import,
                Id = id,
                Import = new ConfigurationImport
                {
                    Id = id,
                    Status = ConfigurationTransferStatus.Queued,
                    FileName = CleanFileName(fileName),
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

    public async Task<ConfigurationImport?> GetImportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(organization, project, view, ConfigurationTransferKind.Import, id, cancellationToken);
        return record?.Import;
    }

    public async Task ProcessAsync(ConfigurationTransferMessage message, CancellationToken cancellationToken = default)
    {
        var record = await _store.GetAsync(message.Organization, message.Project, message.View, message.Kind, message.Id, cancellationToken);

        if (record is null)
        {
            _logger.LogWarning("The {Kind} job {Id} of {Organization}.{Project}.{View} no longer exists.", message.Kind, message.Id, message.Organization, message.Project, message.View);
            return;
        }

        if (record.Status is ConfigurationTransferStatus.Succeeded or ConfigurationTransferStatus.Failed)
        {
            _logger.LogInformation("The {Kind} job {Id} is already {Status}.", message.Kind, message.Id, record.Status);
            return;
        }

        var now = _time.GetUtcNow();
        var running = await _store.TryReplaceAsync(
            Update(
                record,
                export => export with { Status = ConfigurationTransferStatus.Running, StartedAt = now, ExpiresAt = now + _options.Retention },
                import => import with { Status = ConfigurationTransferStatus.Running, StartedAt = now, ExpiresAt = now + _options.Retention }),
            cancellationToken);

        if (running is null)
        {
            _logger.LogInformation("The {Kind} job {Id} was taken by another worker.", message.Kind, message.Id);
            return;
        }

        try
        {
            if (running.Kind == ConfigurationTransferKind.Export)
            {
                await ExportAsync(running, cancellationToken);
            }
            else
            {
                await ImportAsync(running, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "The {Kind} job {Id} of {Organization}.{Project}.{View} failed.", message.Kind, message.Id, message.Organization, message.Project, message.View);
            await FailAsync(message, exception is StorytellerException ? exception.Message : $"The {message.Kind.ToString().ToLowerInvariant()} failed: {exception.Message}", cancellationToken);

            if (message.Kind == ConfigurationTransferKind.Export)
            {
                await TryDeleteArchiveAsync(ConfigurationTransferNames.GetArchiveName(message), cancellationToken);
            }
        }
    }

    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var threshold = _time.GetUtcNow() - _options.Retention - PurgeDelay;
        return _storage.DeleteOlderThanAsync(threshold, cancellationToken);
    }

    private async Task ExportAsync(ConfigurationTransferRecord record, CancellationToken cancellationToken)
    {
        var export = record.Export!;
        var histories = await _configurations.GetConfigurationHistoriesAsync(record.Organization, record.Project, record.View, cancellationToken);

        using var buffer = new MemoryStream();
        var statistics = ConfigurationArchiveWriter.Write(
            buffer,
            new ConfigurationArchiveSource
            {
                Organization = record.Organization,
                Project = record.Project,
                View = record.View,
                ExportedAt = export.StartedAt ?? _time.GetUtcNow(),
                ExportedBy = export.CreatedBy,
                Histories = histories,
            },
            export.Format);

        buffer.Position = 0;
        await _storage.UploadAsync(ConfigurationTransferNames.GetArchiveName(record.ToMessage()), buffer, cancellationToken);

        var now = _time.GetUtcNow();
        var saved = await _store.TryReplaceAsync(
            record with
            {
                Export = export with
                {
                    Status = ConfigurationTransferStatus.Succeeded,
                    CompletedAt = now,
                    ExpiresAt = now + _options.Retention,
                    ConfigurationCount = statistics.ConfigurationCount,
                    VersionCount = statistics.VersionCount,
                    Size = buffer.Length,
                },
            },
            cancellationToken);

        if (saved is null)
        {
            _logger.LogWarning("The export {Id} was changed by another worker; its result was not saved.", record.Id);
        }
    }

    private async Task ImportAsync(ConfigurationTransferRecord record, CancellationToken cancellationToken)
    {
        var import = record.Import!;
        var name = ConfigurationTransferNames.GetArchiveName(record.ToMessage());
        ConfigurationArchiveContent content;

        await using (var blob = await _storage.OpenReadAsync(name, cancellationToken)
                                ?? throw new InvalidOperationException("The uploaded archive no longer exists."))
        {
            using var buffer = new MemoryStream();
            await CopyWithLimitAsync(blob, buffer, _options.MaxArchiveBytes, cancellationToken);
            buffer.Position = 0;
            content = ConfigurationArchiveReader.Read(buffer, _options);
        }

        var progress = new ImportProgress(content.Files.Count, content.IgnoredFiles, _options.MaxReportedItems);
        var current = await SaveImportAsync(record, progress.Apply(import), cancellationToken);
        var savedAt = _time.GetUtcNow();

        if (current is null)
        {
            return;
        }

        foreach (var file in content.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = file.ErrorCode is not null
                ? new ConfigurationImportResult(ConfigurationImportOutcome.Failed, ErrorCode: file.ErrorCode, Message: file.Message)
                : await _configurations.ImportConfigurationAsync(
                    FullKey.Create(file.Key, record.Organization, record.Project, record.View),
                    file.Content!,
                    import.CreatedBy,
                    import.IsForced,
                    import.IsDryRun);

            progress.Add(file, result with { Message = Truncate(result.Message) });

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
            Status = ConfigurationTransferStatus.Succeeded,
            CompletedAt = completedAt,
            ExpiresAt = completedAt + _options.Retention,
        };

        if (await SaveImportAsync(current, completed, cancellationToken) is not null)
        {
            await TryDeleteArchiveAsync(name, cancellationToken);
        }
    }

    private async Task<ConfigurationTransferRecord?> SaveImportAsync(ConfigurationTransferRecord record, ConfigurationImport import, CancellationToken cancellationToken)
    {
        var saved = await _store.TryReplaceAsync(record with { Import = import }, cancellationToken);

        if (saved is null)
        {
            _logger.LogWarning("The import {Id} was changed by another worker or expired; this worker stops.", record.Id);
        }

        return saved;
    }

    private async Task FailAsync(ConfigurationTransferMessage message, string error, CancellationToken cancellationToken)
    {
        try
        {
            var latest = await _store.GetAsync(message.Organization, message.Project, message.View, message.Kind, message.Id, cancellationToken);

            if (latest is null || latest.Status is ConfigurationTransferStatus.Succeeded or ConfigurationTransferStatus.Failed)
            {
                return;
            }

            var now = _time.GetUtcNow();
            await _store.TryReplaceAsync(
                Update(
                    latest,
                    export => export with { Status = ConfigurationTransferStatus.Failed, Error = Truncate(error), CompletedAt = now, ExpiresAt = now + _options.Retention },
                    import => import with { Status = ConfigurationTransferStatus.Failed, Error = Truncate(error), CompletedAt = now, ExpiresAt = now + _options.Retention }),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "The failure of the {Kind} job {Id} was not saved.", message.Kind, message.Id);
        }
    }

    private async Task EnqueueAsync(ConfigurationTransferRecord record, CancellationToken cancellationToken)
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
        var active = await _store.CountActiveAsync(organization, project, _time.GetUtcNow() - ActiveWindow, cancellationToken);

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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "The archive {Name} was not deleted; the purge removes it later.", name);
        }
    }

    private static ConfigurationTransferRecord Update(
        ConfigurationTransferRecord record,
        Func<ConfigurationExport, ConfigurationExport> export,
        Func<ConfigurationImport, ConfigurationImport> import)
    {
        return record.Kind == ConfigurationTransferKind.Export
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

    // Counts of an import while it runs; the reported items stop at the limit.
    private sealed class ImportProgress(int total, int ignoredFiles, int maxItems)
    {
        private readonly List<ConfigurationImportItem> _items = [];
        private int _created;
        private int _updated;
        private int _unchanged;
        private int _skipped;
        private int _failed;
        private bool _truncated;

        public int Processed { get; private set; }

        public void Add(ConfigurationArchiveFile file, ConfigurationImportResult result)
        {
            Processed++;

            switch (result.Outcome)
            {
                case ConfigurationImportOutcome.Created:
                    _created++;
                    break;

                case ConfigurationImportOutcome.Updated:
                    _updated++;
                    break;

                case ConfigurationImportOutcome.Unchanged:
                    _unchanged++;
                    return;

                case ConfigurationImportOutcome.Skipped:
                    _skipped++;
                    break;

                default:
                    _failed++;
                    break;
            }

            if (_items.Count >= maxItems)
            {
                _truncated = true;
                return;
            }

            _items.Add(new ConfigurationImportItem
            {
                Path = file.Path,
                AnnotationKey = file.Key.ToString(),
                Outcome = result.Outcome,
                Version = result.Version,
                ErrorCode = result.ErrorCode,
                Message = result.Message,
            });
        }

        public ConfigurationImport Apply(ConfigurationImport import)
        {
            return import with
            {
                TotalCount = total,
                ProcessedCount = Processed,
                Summary = new ConfigurationImportSummary
                {
                    Created = _created,
                    Updated = _updated,
                    Unchanged = _unchanged,
                    Skipped = _skipped,
                    Failed = _failed,
                    IgnoredFiles = ignoredFiles,
                },
                Items = _items.ToList(),
                AreItemsTruncated = _truncated,
            };
        }
    }
}
