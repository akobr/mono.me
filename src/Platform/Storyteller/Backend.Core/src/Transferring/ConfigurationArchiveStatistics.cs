namespace _42.Platform.Storyteller.Transferring;

// Keys with a current document, and every written version.
public sealed record class ConfigurationArchiveStatistics(int ConfigurationCount, int VersionCount);
