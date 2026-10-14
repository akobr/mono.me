namespace _42.Platform.Storyteller.Transferring.Git;

// A file of the working tree in .git/index.
public sealed record class GitIndexEntry(string Path, byte[] Id, long Size, DateTimeOffset ModifiedAt);
