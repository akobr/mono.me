using System.Globalization;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Transferring;

// Reads the document files of a zip by their names (ViewArchiveNames), in any folder, so a flat zip, the folders of
// an export and a repository read alike. Hidden paths (.git, .github, .DS_Store, ._*) and __MACOSX are skipped
// without being counted. A file of a kind the include filter leaves out is counted as excluded and never
// decompressed; version files, export.json and names that are not documents are counted as ignored.
public static class ViewArchiveReader
{
    // Checks the central directory only, before anything is decompressed; every kind counts against the limits.
    public static void Validate(Stream archive, ViewTransferOptions options)
    {
        using var zip = Open(archive);
        _ = Inspect(zip, options, ViewArchiveInclude.All);
    }

    // The include filter: null or empty means all four kinds.
    public static ViewArchiveDocuments Read(Stream archive, ViewTransferOptions options, IEnumerable<ViewArchiveContent>? include = null)
    {
        using var zip = Open(archive);
        var inspection = Inspect(zip, options, ViewArchiveInclude.Resolve(include));
        var files = new List<ViewArchiveFile>(inspection.Entries.Count);
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        // one document per identity, whatever the folders and the spelling of the key: the first path wins
        foreach (var (path, name, entry) in inspection.Entries)
        {
            if (seen.TryGetValue(name.Identity, out var first))
            {
                var document = ViewArchiveNames.Describe(name.Kind, name.AnnotationType, name.AnnotationKey?.ToString());
                files.Add(Failed(path, name, ErrorCodes.DuplicateFile, $"The file '{first}' already holds the {document}."));
                continue;
            }

            seen[name.Identity] = path;
            files.Add(name.ErrorCode is not null
                ? Failed(path, name, name.ErrorCode, name.Message ?? "The file cannot be imported.")
                : ReadFile(path, name, entry, options.MaxDocumentBytes));
        }

        return new ViewArchiveDocuments(files, inspection.Ignored, inspection.Excluded);
    }

    private static ZipArchive Open(Stream archive)
    {
        try
        {
            return new ZipArchive(archive, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidInputException($"The archive is not a valid zip: {exception.Message}", ErrorCodes.InvalidArchive);
        }
    }

    // The document files of the included kinds in path order, and the counts of the other files. The limits apply
    // to the included files together.
    private static Inspection Inspect(ZipArchive zip, ViewTransferOptions options, IReadOnlyList<ViewArchiveContent> include)
    {
        if (zip.Entries.Count > options.MaxArchiveEntries)
        {
            throw new InvalidInputException(
                $"The archive has {zip.Entries.Count} entries, at most {options.MaxArchiveEntries} are allowed.",
                ErrorCodes.InvalidArchive);
        }

        var entries = new List<(string Path, ViewArchiveName Name, ZipArchiveEntry Entry)>();
        var ignored = 0;
        var excluded = 0;
        var declared = 0L;

        foreach (var entry in zip.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');

            if (path.EndsWith('/') || ViewArchiveNames.IsSkippedPath(path))
            {
                continue;
            }

            var fileName = path[(path.LastIndexOf('/') + 1)..];

            // an import never replays a history, so version files are ignored whatever their kind
            if (!ViewArchiveNames.TryParse(fileName, out var name) || name.Version.HasValue)
            {
                ignored++;
                continue;
            }

            if (!include.Contains(name.Content))
            {
                excluded++;
                continue;
            }

            declared += entry.Length;

            if (declared > options.MaxExtractedBytes)
            {
                throw new InvalidInputException(
                    $"The document files of the archive are larger than {options.MaxExtractedBytes.ToString(CultureInfo.InvariantCulture)} bytes uncompressed.",
                    ErrorCodes.InvalidArchive);
            }

            entries.Add((path, name, entry));
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return new Inspection(entries, ignored, excluded);
    }

    private static ViewArchiveFile ReadFile(string path, ViewArchiveName name, ZipArchiveEntry entry, int maxBytes)
    {
        if (entry.Length > maxBytes)
        {
            return TooLarge(path, name, maxBytes);
        }

        byte[] bytes;

        try
        {
            // the declared size can lie, so the read stops one byte after the limit
            using var stream = entry.Open();
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;

            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                buffer.Write(chunk, 0, read);

                if (buffer.Length > maxBytes)
                {
                    return TooLarge(path, name, maxBytes);
                }
            }

            bytes = buffer.ToArray();
        }
        catch (InvalidDataException exception)
        {
            return Failed(path, name, ErrorCodes.InvalidDocument, $"The file cannot be decompressed: {exception.Message}");
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return File(path, name) with { Content = Parse(stream, name.Kind) };
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            return Failed(path, name, ErrorCodes.InvalidDocument, $"The file is not a JSON object: {exception.Message}");
        }
    }

    // Each kind with the date handling of its reads, so an exported document reads back equal (ViewDocumentJson).
    private static JObject Parse(Stream stream, ViewDocumentKind kind)
    {
        return kind == ViewDocumentKind.Annotation
            ? ViewAnnotationDocument.Parse(stream)
            : ViewDocumentJson.Parse(stream);
    }

    private static ViewArchiveFile TooLarge(string path, ViewArchiveName name, int maxBytes)
    {
        return Failed(path, name, ErrorCodes.InvalidDocument, $"The file is larger than {maxBytes.ToString(CultureInfo.InvariantCulture)} bytes.");
    }

    private static ViewArchiveFile Failed(string path, ViewArchiveName name, string errorCode, string message)
    {
        return File(path, name) with { ErrorCode = errorCode, Message = message };
    }

    private static ViewArchiveFile File(string path, ViewArchiveName name)
    {
        return new ViewArchiveFile { Path = path, Kind = name.Kind, AnnotationType = name.AnnotationType, AnnotationKey = name.AnnotationKey };
    }

    private sealed record class Inspection(
        IReadOnlyList<(string Path, ViewArchiveName Name, ZipArchiveEntry Entry)> Entries,
        int Ignored,
        int Excluded);
}
