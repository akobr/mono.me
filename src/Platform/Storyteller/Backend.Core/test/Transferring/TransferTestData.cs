using System.IO.Compression;
using System.Text;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

internal static class TransferTestData
{
    public static readonly DateTimeOffset Start = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);

    public static readonly AuditActor Actor = new() { Kind = AuditActorKind.Account, Id = "user_01", Name = "Ada" };

    // versions: (version, author, minutes after Start, JSON); current: whether the last one is the current document
    public static ConfigurationHistory History(string key, bool hasCurrent, params (ulong Version, string Author, int Minutes, string Json)[] versions)
    {
        var documents = Documents(versions);

        return new ConfigurationHistory
        {
            AnnotationKey = key,
            Versions = documents,
            Current = hasCurrent ? documents[^1] : null,
        };
    }

    public static TemplateHistory Template(string type, bool hasCurrent, params (ulong Version, string Author, int Minutes, string Json)[] versions)
    {
        var documents = Documents(versions);

        return new TemplateHistory
        {
            AnnotationType = type,
            Versions = documents,
            Current = hasCurrent ? documents[^1] : null,
        };
    }

    // type: the type of a type or descendant-type schema; key: the annotation of an annotation schema or the
    // ancestor of a descendant-type schema
    public static SchemaHistory Schema(
        ViewDocumentKind kind,
        string? type,
        string? key,
        bool hasCurrent,
        params (ulong Version, string Author, int Minutes, string Json)[] versions)
    {
        var documents = Documents(versions);

        return new SchemaHistory
        {
            Kind = kind,
            AnnotationType = type,
            AnnotationKey = key,
            Versions = documents,
            Current = hasCurrent ? documents[^1] : null,
        };
    }

    // An active annotation, created and modified at the minutes after Start.
    public static Annotation ActiveAnnotation(string key, int createdMinutes = 0, int? modifiedMinutes = null, string? title = null)
    {
        var annotation = AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), "main", "default");

        return annotation with
        {
            Title = title,
            Created = AuditStamp.Of(Actor, Start.AddMinutes(createdMinutes)),
            Modified = AuditStamp.Of(AuditActor.Machine("machine-1"), Start.AddMinutes(modifiedMinutes ?? createdMinutes)),
        };
    }

    public static ViewArchiveSource Source(params ConfigurationHistory[] histories)
    {
        return new ViewArchiveSource
        {
            Organization = "acme",
            Project = "main",
            View = "default",
            ExportedAt = Start.AddDays(1),
            ExportedBy = Actor,
            Configurations = histories,
        };
    }

    public static byte[] Write(ViewArchiveFormat format, params ConfigurationHistory[] histories)
    {
        return Write(Source(histories), format);
    }

    public static byte[] Write(ViewArchiveSource source, ViewArchiveFormat format = ViewArchiveFormat.Folder)
    {
        using var stream = new MemoryStream();
        ViewArchiveWriter.Write(stream, source, format);
        return stream.ToArray();
    }

    public static Dictionary<string, string> ReadTextEntries(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith('/') && !entry.FullName.StartsWith(".git/", StringComparison.Ordinal)))
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            entries[entry.FullName] = reader.ReadToEnd();
        }

        return entries;
    }

    // A zip of text files, for the reader.
    public static MemoryStream Zip(params (string Path, string Content)[] files)
    {
        var stream = new MemoryStream();

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in files)
            {
                var entry = archive.CreateEntry(path);

                if (!path.EndsWith('/'))
                {
                    using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                    writer.Write(content);
                }
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static List<ConfigurationDocument> Documents((ulong Version, string Author, int Minutes, string Json)[] versions)
    {
        return versions
            .Select(version => new ConfigurationDocument
            {
                Version = version.Version,
                Author = version.Author,
                CreatedAt = Start.AddMinutes(version.Minutes),
                Content = JObject.Parse(version.Json),
            })
            .ToList();
    }
}
