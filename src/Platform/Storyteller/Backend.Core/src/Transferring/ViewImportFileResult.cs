namespace _42.Platform.Storyteller.Transferring;

// One file of an import with its outcome, as the importer yields them in the order of the passes.
public sealed record class ViewImportFileResult(ViewArchiveFile File, ViewImportResult Result);
