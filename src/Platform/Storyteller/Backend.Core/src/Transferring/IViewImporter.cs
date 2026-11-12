namespace _42.Platform.Storyteller.Transferring;

// The import of document files into a view without a job: what an import job runs on the files of its archive, and
// what a tool runs on files it read itself (ViewArchiveReader) or made in memory.
public interface IViewImporter
{
    // The passes of an import over the files, in any input order: one pre-read per kind, the annotations (ancestors
    // first), the overlay, then schemas, templates and configurations. Yields each outcome after the overlay has
    // tracked it; stopping the enumeration stops the import. A file with an ErrorCode is Failed without a call; a write
    // conflict fails its file (Conflict); any other exception ends the enumeration. A given overlay is used and filled
    // (a caller can share one over sequential calls); without one, the call makes its own. Nothing runs before the
    // enumeration starts, and one call must not be enumerated twice at once.
    IAsyncEnumerable<ViewImportFileResult> ImportAsync(
        string organization,
        string project,
        string view,
        IReadOnlyList<ViewArchiveFile> files,
        ViewImportOptions options,
        ViewImportOverlay? overlay = null,
        CancellationToken cancellationToken = default);
}
