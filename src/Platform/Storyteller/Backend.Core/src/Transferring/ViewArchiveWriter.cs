using System.IO.Compression;
using _42.Platform.Storyteller.Transferring.Git;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Transferring;

// Writes an export as a zip: the folder format with one folder per included kind, version files and export.json, or
// a git repository with the same folders and one history of every version. A kind without documents gets no folder,
// and a history without versions writes nothing.
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
                ? WriteGit(archive, source, content)
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
        return GetStatistics(content);
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

    // One history of every included document in the folders of the folder format. It is ordered by time and, within
    // one time, like the import: by kind (annotations, schemas, templates, configurations), then name. Each document's
    // versions stay in order even when their times are not. The last commit removes the deleted schemas, templates
    // and configurations, whose last version is in the tree.
    private static ViewArchiveStatistics WriteGit(ZipArchive archive, ViewArchiveSource source, Content content)
    {
        var documents = GetGitDocuments(source, content);
        var repository = new GitRepositoryWriter();

        // a merge of the documents: the next commit of each document waits in the queue, by its time, then by the
        // position of its document
        var queue = new PriorityQueue<(int Document, int Commit), (DateTimeOffset At, int Document)>();

        for (var index = 0; index < documents.Count; index++)
        {
            Enqueue(index, 0);
        }

        while (queue.TryDequeue(out var next, out _))
        {
            var document = documents[next.Document];
            var commit = document.Commits[next.Commit];
            repository.Commit([(document.Path, ViewDocumentJson.Serialize(commit.Document))], commit.Signature, commit.Message);
            Enqueue(next.Document, next.Commit + 1);
        }

        var deleted = documents
            .Where(document => document.IsDeleted)
            .Select(document => document.Path)
            .ToList();

        if (deleted.Count > 0)
        {
            repository.Commit(
                deleted.Select(path => (path, (byte[]?)null)),
                GitSignature.System(source.ExportedAt),
                $"Remove deleted documents\n\n{string.Join('\n', deleted)}");
        }

        repository.WriteTo(archive, source.ExportedAt);
        return GetStatistics(content);

        void Enqueue(int document, int commit)
        {
            var commits = documents[document].Commits;

            if (commit < commits.Count)
            {
                queue.Enqueue((document, commit), (commits[commit].Signature.When, document));
            }
        }
    }

    // The documents of the history in import order: annotations by depth in the graph, type and key (ancestors
    // first); then type, descendant-type and annotation schemas, each by type and key; templates by type;
    // configurations by key.
    private static List<GitDocument> GetGitDocuments(ViewArchiveSource source, Content content)
    {
        var annotations = content.Annotations
            .OrderBy(annotation => AnnotationKey.TryParse(annotation.AnnotationKey, out var key) ? key.GetDepth() : 0)
            .ThenBy(annotation => annotation.AnnotationType)
            .ThenBy(annotation => annotation.AnnotationKey, StringComparer.Ordinal)
            .Select(annotation =>
            {
                // the commit is the creation of the key, its time and actor; the content is the current one
                var created = annotation.Created ?? annotation.Modified;
                var at = created?.At ?? source.ExportedAt;
                var commit = new GitCommit(
                    GitSignature.FromActor(created?.By, at),
                    $"Add {ViewArchiveNames.Describe(ViewDocumentKind.Annotation, null, annotation.AnnotationKey)}",
                    ViewAnnotationDocument.Write(annotation));

                return new GitDocument(ViewArchiveNames.GetPath(ViewDocumentKind.Annotation, null, annotation.AnnotationKey), [commit], false);
            });

        return annotations
            .Concat(content.Schemas.Select(history => GetGitDocument(history.Kind, history.AnnotationType, history.AnnotationKey, history.Versions, history.Current)))
            .Concat(content.Templates.Select(history => GetGitDocument(ViewDocumentKind.Template, history.AnnotationType, null, history.Versions, history.Current)))
            .Concat(content.Configurations.Select(history => GetGitDocument(ViewDocumentKind.Configuration, null, history.AnnotationKey, history.Versions, history.Current)))
            .ToList();
    }

    // One commit per version, by the version's actor (or author) at its time: "Add <document> (version n)" for the
    // first version in the export, "Update <document> to version n" for the next ones.
    private static GitDocument GetGitDocument(
        ViewDocumentKind kind,
        string? annotationType,
        string? annotationKey,
        IReadOnlyList<ConfigurationDocument> versions,
        ConfigurationDocument? current)
    {
        var description = ViewArchiveNames.Describe(kind, annotationType, annotationKey);
        var commits = versions
            .Select((version, index) => new GitCommit(
                version.Actor is not null
                    ? GitSignature.FromActor(version.Actor, version.CreatedAt)
                    : GitSignature.FromAuthor(version.Author, version.CreatedAt),
                index == 0 ? $"Add {description} (version {version.Version})" : $"Update {description} to version {version.Version}",
                version.Content))
            .ToList();

        return new GitDocument(ViewArchiveNames.GetPath(kind, annotationType, annotationKey), commits, current is null);
    }

    private static ViewArchiveStatistics GetStatistics(Content content)
    {
        return new ViewArchiveStatistics(
            content.Annotations.Count,
            content.Schemas.Count(history => history.Current is not null),
            content.Templates.Count(history => history.Current is not null),
            content.Configurations.Count(history => history.Current is not null),
            content.Schemas.Sum(history => history.Versions.Count)
            + content.Templates.Sum(history => history.Versions.Count)
            + content.Configurations.Sum(history => history.Versions.Count));
    }

    private static void WriteEntry(ZipArchive archive, string name, byte[] content, DateTimeOffset modifiedAt)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = GitRepositoryWriter.ZipTime(modifiedAt);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private sealed record class Content(
        IReadOnlyList<ViewArchiveContent> Include,
        IReadOnlyList<Annotation> Annotations,
        IReadOnlyList<SchemaHistory> Schemas,
        IReadOnlyList<TemplateHistory> Templates,
        IReadOnlyList<ConfigurationHistory> Configurations);

    // A document of the git history at its path, with its commits in order; a deleted one is removed at the end.
    private sealed record class GitDocument(string Path, IReadOnlyList<GitCommit> Commits, bool IsDeleted);

    private sealed record class GitCommit(GitSignature Signature, string Message, JToken Document);

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
