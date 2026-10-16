using System.IO.Compression;
using _42.Platform.Storyteller.Transferring.Git;

namespace _42.Platform.Storyteller.Transferring;

// Writes an export as a zip: the folder format with one folder per included kind, version files and export.json, or
// a git repository. A kind without documents gets no folder, and a history without versions writes nothing.
// Transitional: the git format writes the configurations only, flat, until it builds nested trees (1.8).
public static class ViewArchiveWriter
{
    public const int ManifestVersion = 2;

    public static ViewArchiveStatistics Write(Stream output, ViewArchiveSource source, ViewArchiveFormat format)
    {
        var content = Select(source);
        ViewArchiveStatistics statistics;

        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            statistics = format == ViewArchiveFormat.Git
                ? WriteGit(archive, source, content.Configurations)
                : WriteFolder(archive, source, content);
        }

        return statistics;
    }

    // The documents of the included kinds, in a stable order: annotations by key; schemas by kind (type, descendant
    // type, annotation), type and key; templates by type; configurations by key.
    private static Content Select(ViewArchiveSource source)
    {
        var include = ViewArchiveInclude.Resolve(source.Include);

        return new Content(
            include,
            include.Contains(ViewArchiveContent.Annotations)
                ? source.Annotations.OrderBy(annotation => annotation.AnnotationKey, StringComparer.Ordinal).ToList()
                : [],
            include.Contains(ViewArchiveContent.Schemas)
                ? source.Schemas
                    .Where(history => history.Versions.Count > 0)
                    .OrderBy(history => history.Kind)
                    .ThenBy(history => history.AnnotationType, StringComparer.Ordinal)
                    .ThenBy(history => history.AnnotationKey, StringComparer.Ordinal)
                    .ToList()
                : [],
            include.Contains(ViewArchiveContent.Templates)
                ? source.Templates
                    .Where(history => history.Versions.Count > 0)
                    .OrderBy(history => history.AnnotationType, StringComparer.Ordinal)
                    .ToList()
                : [],
            include.Contains(ViewArchiveContent.Configurations)
                ? source.Configurations
                    .Where(history => history.Versions.Count > 0)
                    .OrderBy(history => history.AnnotationKey, StringComparer.Ordinal)
                    .ToList()
                : []);
    }

    private static ViewArchiveStatistics WriteFolder(ZipArchive archive, ViewArchiveSource source, Content content)
    {
        var annotations = new List<ManifestAnnotation>(content.Annotations.Count);

        foreach (var annotation in content.Annotations)
        {
            var path = ViewArchiveNames.GetPath(ViewDocumentKind.Annotation, null, annotation.AnnotationKey);
            var modifiedAt = annotation.Modified?.At ?? annotation.Created?.At ?? source.ExportedAt;
            WriteEntry(archive, path, ViewDocumentJson.Serialize(ViewAnnotationDocument.Write(annotation)), modifiedAt);
            annotations.Add(new ManifestAnnotation(annotation.AnnotationKey, path, annotation.Created, annotation.Modified));
        }

        var schemas = new List<ManifestSchema>(content.Schemas.Count);

        foreach (var history in content.Schemas)
        {
            var files = WriteHistory(archive, history.Kind, history.AnnotationType, history.AnnotationKey, history.Versions, history.Current);
            schemas.Add(new ManifestSchema(
                history.Kind,
                history.AnnotationType,
                history.AnnotationKey,
                files.Current,
                history.Current?.Version,
                history.Current?.Author,
                history.Current?.CreatedAt,
                files.Versions));
        }

        var templates = new List<ManifestTemplate>(content.Templates.Count);

        foreach (var history in content.Templates)
        {
            var files = WriteHistory(archive, ViewDocumentKind.Template, history.AnnotationType, null, history.Versions, history.Current);
            templates.Add(new ManifestTemplate(
                history.AnnotationType,
                files.Current,
                history.Current?.Version,
                history.Current?.Author,
                history.Current?.CreatedAt,
                files.Versions));
        }

        var configurations = new List<ManifestConfiguration>(content.Configurations.Count);

        foreach (var history in content.Configurations)
        {
            var files = WriteHistory(archive, ViewDocumentKind.Configuration, null, history.AnnotationKey, history.Versions, history.Current);
            configurations.Add(new ManifestConfiguration(
                history.AnnotationKey,
                files.Current,
                history.Current?.Version,
                history.Current?.Author,
                history.Current?.CreatedAt,
                files.Versions));
        }

        var manifest = new Manifest(
            ManifestVersion,
            source.Organization,
            source.Project,
            source.View,
            source.ExportedAt,
            source.ExportedBy,
            content.Include,
            annotations,
            schemas,
            templates,
            configurations);

        WriteEntry(archive, ViewArchiveNames.ManifestFileName, ViewDocumentJson.SerializeManifest(manifest), source.ExportedAt);

        return new ViewArchiveStatistics(
            content.Annotations.Count,
            content.Schemas.Count(history => history.Current is not null),
            content.Templates.Count(history => history.Current is not null),
            content.Configurations.Count(history => history.Current is not null),
            content.Schemas.Sum(history => history.Versions.Count)
            + content.Templates.Sum(history => history.Versions.Count)
            + content.Configurations.Sum(history => history.Versions.Count));
    }

    // The current document and the earlier versions of one history, next to each other in the folder of its kind.
    // Each entry has the time of its version.
    private static (string? Current, IReadOnlyList<ManifestVersionFile> Versions) WriteHistory(
        ZipArchive archive,
        ViewDocumentKind kind,
        string? annotationType,
        string? annotationKey,
        IReadOnlyList<ConfigurationDocument> versions,
        ConfigurationDocument? current)
    {
        string? currentPath = null;
        var earlier = new List<ManifestVersionFile>(versions.Count);

        for (var index = 0; index < versions.Count; index++)
        {
            var version = versions[index];
            var isCurrent = current is not null && index == versions.Count - 1;
            var path = ViewArchiveNames.GetPath(kind, annotationType, annotationKey, isCurrent ? null : version.Version);
            WriteEntry(archive, path, ViewDocumentJson.Serialize(version.Content), version.CreatedAt);

            if (isCurrent)
            {
                currentPath = path;
            }
            else
            {
                earlier.Add(new ManifestVersionFile(version.Version, path, version.Author, version.CreatedAt));
            }
        }

        return (currentPath, earlier);
    }

    private static ViewArchiveStatistics WriteGit(ZipArchive archive, ViewArchiveSource source, IReadOnlyList<ConfigurationHistory> histories)
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
            var fileName = ViewArchiveNames.GetConfigurationFileName(history.AnnotationKey);
            var message = started.Add(history.AnnotationKey)
                ? $"Add {history.AnnotationKey} (version {version.Version})"
                : $"Update {history.AnnotationKey} to version {version.Version}";

            repository.Commit(
                [(fileName, ViewDocumentJson.Serialize(version.Content))],
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
                cleared.Select(key => (ViewArchiveNames.GetConfigurationFileName(key), (byte[]?)null)),
                GitSignature.System(source.ExportedAt),
                $"Remove cleared configurations\n\n{string.Join('\n', cleared)}");
        }

        repository.WriteTo(archive, source.ExportedAt);

        return new ViewArchiveStatistics(
            0,
            0,
            0,
            histories.Count(history => history.Current is not null),
            histories.Sum(history => history.Versions.Count));

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

    private sealed record class Content(
        IReadOnlyList<ViewArchiveContent> Include,
        IReadOnlyList<Annotation> Annotations,
        IReadOnlyList<SchemaHistory> Schemas,
        IReadOnlyList<TemplateHistory> Templates,
        IReadOnlyList<ConfigurationHistory> Configurations);

    // export.json, version 2. Every kind is listed, an excluded one empty; the import never reads it.
    private sealed record class Manifest(
        int ManifestVersion,
        string Organization,
        string Project,
        string View,
        DateTimeOffset ExportedAt,
        AuditActor ExportedBy,
        IReadOnlyList<ViewArchiveContent> Include,
        IReadOnlyList<ManifestAnnotation> Annotations,
        IReadOnlyList<ManifestSchema> Schemas,
        IReadOnlyList<ManifestTemplate> Templates,
        IReadOnlyList<ManifestConfiguration> Configurations);

    // The stamps are here for information only: an import neither reads nor restores them.
    private sealed record class ManifestAnnotation(string Key, string File, AuditStamp? Created, AuditStamp? Modified);

    // A deleted schema, template or configuration has versions but no current document: File and Version are null.
    private sealed record class ManifestSchema(
        ViewDocumentKind Kind,
        string? AnnotationType,
        string? AnnotationKey,
        string? File,
        ulong? Version,
        string? Author,
        DateTimeOffset? UpdatedAt,
        IReadOnlyList<ManifestVersionFile> Versions);

    private sealed record class ManifestTemplate(
        string AnnotationType,
        string? File,
        ulong? Version,
        string? Author,
        DateTimeOffset? UpdatedAt,
        IReadOnlyList<ManifestVersionFile> Versions);

    private sealed record class ManifestConfiguration(
        string AnnotationKey,
        string? File,
        ulong? Version,
        string? Author,
        DateTimeOffset? UpdatedAt,
        IReadOnlyList<ManifestVersionFile> Versions);

    // An earlier version, next to the current file.
    private sealed record class ManifestVersionFile(ulong Version, string File, string Author, DateTimeOffset CreatedAt);
}
