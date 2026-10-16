namespace _42.Platform.Storyteller.Transferring;

// The outcome of importing one document; a dry run reports Created and Updated without a version. Warnings
// accompany a written (or, in a dry run, a writable) document.
public sealed record class ViewImportResult(
    ViewImportOutcome Outcome,
    ulong? Version = null,
    string? ErrorCode = null,
    string? Message = null,
    IReadOnlyList<ViewImportWarning>? Warnings = null);
