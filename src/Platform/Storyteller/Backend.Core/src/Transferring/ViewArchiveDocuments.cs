namespace _42.Platform.Storyteller.Transferring;

// The document files of the included kinds of an uploaded archive, in path order; the files of the kinds left out
// (ExcludedFiles); and the version files, export.json and other names (IgnoredFiles).
public sealed record class ViewArchiveDocuments(IReadOnlyList<ViewArchiveFile> Files, int IgnoredFiles, int ExcludedFiles);
