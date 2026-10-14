using System.IO.Compression;
using _42.Platform.Storyteller.Transferring.Git;

namespace _42.Platform.Storyteller.Transferring;

// Writes an export as a zip: the folder format with version files and export.json, or a git repository.
public static class ConfigurationArchiveWriter
{
    public static ConfigurationArchiveStatistics Write(Stream output, ConfigurationArchiveSource source, ConfigurationArchiveFormat format)
    {
        var histories = source.Histories
            .Where(history => history.Versions.Count > 0)
            .OrderBy(history => history.AnnotationKey, StringComparer.Ordinal)
            .ToList();

        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (format == ConfigurationArchiveFormat.Git)
            {
                WriteGit(archive, source, histories);
            }
            else
            {
                WriteFolder(archive, source, histories);
            }
        }

        return new ConfigurationArchiveStatistics(
            histories.Count(history => history.Current is not null),
            histories.Sum(history => history.Versions.Count));
    }

    private static void WriteFolder(ZipArchive archive, ConfigurationArchiveSource source, IReadOnlyList<ConfigurationHistory> histories)
    {
        var configurations = new List<ManifestConfiguration>(histories.Count);

        foreach (var history in histories)
        {
            var versions = new List<ManifestVersion>(history.Versions.Count);

            for (var index = 0; index < history.Versions.Count; index++)
            {
                var version = history.Versions[index];
                var isCurrent = history.Current is not null && index == history.Versions.Count - 1;
                var fileName = ConfigurationFileNames.GetFileName(history.AnnotationKey, isCurrent ? null : version.Version);
                WriteEntry(archive, fileName, ConfigurationDocumentJson.Serialize(version.Content), version.CreatedAt);

                if (!isCurrent)
                {
                    versions.Add(new ManifestVersion(version.Version, fileName, version.Author, version.CreatedAt));
                }
            }

            configurations.Add(new ManifestConfiguration(
                history.AnnotationKey,
                history.Current is null ? null : ConfigurationFileNames.GetFileName(history.AnnotationKey),
                history.Current?.Version,
                history.Current?.Author,
                history.Current?.CreatedAt,
                versions));
        }

        var manifest = new Manifest(
            nameof(ConfigurationArchiveFormat.Folder),
            source.Organization,
            source.Project,
            source.View,
            source.ExportedAt,
            source.ExportedBy,
            configurations);

        WriteEntry(archive, ConfigurationFileNames.ManifestFileName, ConfigurationDocumentJson.SerializeManifest(manifest), source.ExportedAt);
    }

    private static void WriteGit(ZipArchive archive, ConfigurationArchiveSource source, IReadOnlyList<ConfigurationHistory> histories)
    {
        var repository = new GitRepositoryWriter();
        var started = new HashSet<string>(StringComparer.Ordinal);

        // a merge of the per-key histories by time: each key's versions stay in version order
        var queue = new PriorityQueue<(int History, int Version), (DateTimeOffset At, string Key, ulong Version)>(VersionOrder.Instance);

        for (var index = 0; index < histories.Count; index++)
        {
            Enqueue(index, 0);
        }

        while (queue.TryDequeue(out var next, out _))
        {
            var history = histories[next.History];
            var version = history.Versions[next.Version];
            var fileName = ConfigurationFileNames.GetFileName(history.AnnotationKey);
            var message = started.Add(history.AnnotationKey)
                ? $"Add {history.AnnotationKey} (version {version.Version})"
                : $"Update {history.AnnotationKey} to version {version.Version}";

            repository.Commit(
                [(fileName, ConfigurationDocumentJson.Serialize(version.Content))],
                GitSignature.FromAuthor(version.Author, version.CreatedAt),
                message);

            Enqueue(next.History, next.Version + 1);
        }

        // cleared configurations: their last version is in the tree, the current state has no document
        var cleared = histories
            .Where(history => history.Current is null)
            .Select(history => history.AnnotationKey)
            .ToList();

        if (cleared.Count > 0)
        {
            repository.Commit(
                cleared.Select(key => (ConfigurationFileNames.GetFileName(key), (byte[]?)null)),
                GitSignature.System(source.ExportedAt),
                $"Remove cleared configurations\n\n{string.Join('\n', cleared)}");
        }

        repository.WriteTo(archive, source.ExportedAt);

        void Enqueue(int history, int version)
        {
            var versions = histories[history].Versions;

            if (version < versions.Count)
            {
                queue.Enqueue((history, version), (versions[version].CreatedAt, histories[history].AnnotationKey, versions[version].Version));
            }
        }
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content, DateTimeOffset modifiedAt)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = GitRepositoryWriter.ZipTime(modifiedAt);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private sealed class VersionOrder : IComparer<(DateTimeOffset At, string Key, ulong Version)>
    {
        public static readonly VersionOrder Instance = new();

        public int Compare((DateTimeOffset At, string Key, ulong Version) x, (DateTimeOffset At, string Key, ulong Version) y)
        {
            var byTime = x.At.CompareTo(y.At);

            if (byTime != 0)
            {
                return byTime;
            }

            var byKey = string.CompareOrdinal(x.Key, y.Key);
            return byKey != 0 ? byKey : x.Version.CompareTo(y.Version);
        }
    }

    private sealed record class Manifest(
        string Format,
        string Organization,
        string Project,
        string View,
        DateTimeOffset ExportedAt,
        AuditActor ExportedBy,
        IReadOnlyList<ManifestConfiguration> Configurations);

    private sealed record class ManifestConfiguration(
        string AnnotationKey,
        string? File,
        ulong? Version,
        string? Author,
        DateTimeOffset? UpdatedAt,
        IReadOnlyList<ManifestVersion> Versions);

    private sealed record class ManifestVersion(ulong Version, string File, string Author, DateTimeOffset CreatedAt);
}
