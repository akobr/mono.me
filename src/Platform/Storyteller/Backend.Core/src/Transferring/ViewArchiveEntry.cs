namespace _42.Platform.Storyteller.Transferring;

// A file of a zip or a folder for the reader: its path below the root ('/' or '\' separators), its size as declared,
// and how to open it. The reader opens only the documents it reads, so the caller keeps the file system to itself.
public sealed record class ViewArchiveEntry(string Path, long Length, Func<Stream> Open);
