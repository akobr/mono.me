namespace _42.Platform.Cli.AutomationTests.Tests.General;

// Checks of the Context and Errors cases that SformResult does not offer.
internal static class GeneralChecks
{
    // The files of a folder and below, with their sizes and times of the last write; empty when it does not exist.
    public static IReadOnlyDictionary<string, (long Length, DateTime Written)> Snapshot(System.IO.Abstractions.IFileSystem fileSystem, string directory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (!fileSystem.Directory.Exists(directory))
        {
            return new Dictionary<string, (long, DateTime)>();
        }

        return fileSystem.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                file => file,
                file =>
                {
                    var info = fileSystem.FileInfo.New(file);
                    return (info.Length, info.LastWriteTimeUtc);
                },
                StringComparer.OrdinalIgnoreCase);
    }

    // The files that are new or changed in after, relative to the folder.
    public static IReadOnlyList<string> Changes(IReadOnlyDictionary<string, (long Length, DateTime Written)> before, IReadOnlyDictionary<string, (long Length, DateTime Written)> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        return after
            .Where(entry => !before.TryGetValue(entry.Key, out var earlier) || earlier != entry.Value)
            .Select(entry => entry.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
