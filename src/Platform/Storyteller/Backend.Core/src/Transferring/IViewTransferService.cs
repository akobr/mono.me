namespace _42.Platform.Storyteller.Transferring;

// Exports and imports of the documents of a view, as jobs that a queue-triggered worker processes.
public interface IViewTransferService
{
    Task<ViewExport> StartExportAsync(
        string organization,
        string project,
        string view,
        ViewArchiveFormat format,
        AuditActor actor,
        CancellationToken cancellationToken = default);

    Task<ViewExport?> GetExportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default);

    // The archive of a succeeded export; NotFoundException for an unknown or expired one, ConflictException
    // (TransferNotReady) while it is not ready.
    Task<(ViewExport Export, Stream Archive)> OpenExportArchiveAsync(
        string organization,
        string project,
        string view,
        string id,
        CancellationToken cancellationToken = default);

    // The archive must be seekable; it is checked (InvalidInputException with InvalidArchive) before it is stored.
    Task<ViewImport> StartImportAsync(
        string organization,
        string project,
        string view,
        Stream archive,
        string? fileName,
        bool force,
        bool dryRun,
        AuditActor actor,
        CancellationToken cancellationToken = default);

    Task<ViewImport?> GetImportAsync(string organization, string project, string view, string id, CancellationToken cancellationToken = default);

    // Runs one job; a repeated message of a finished job does nothing.
    Task ProcessAsync(ViewTransferMessage message, CancellationToken cancellationToken = default);

    // Removes archives older than the retention and returns how many.
    Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default);
}
