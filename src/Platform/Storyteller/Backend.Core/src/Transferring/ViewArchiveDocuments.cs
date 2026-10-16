namespace _42.Platform.Storyteller.Transferring;

// The document files of an uploaded archive, in path order, and how many other files it has.
public sealed record class ViewArchiveDocuments(IReadOnlyList<ViewArchiveFile> Files, int IgnoredFiles);
