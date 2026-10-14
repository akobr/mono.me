namespace _42.Platform.Storyteller.Transferring;

// The configuration files of an uploaded archive, in path order, and how many other files it has.
public sealed record class ConfigurationArchiveContent(IReadOnlyList<ConfigurationArchiveFile> Files, int IgnoredFiles);
