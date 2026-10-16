using System.Globalization;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Transferring;

// Reads the document files of a zip by their names (ViewArchiveNames), in any folder, so a flat zip, a zipped
// folder and a repository work alike. Hidden paths (.git, ._*) and __MACOSX are skipped.
// Transitional: only the current documents of configurations (<key>.conf.json) are read; every other name counts
// as ignored until the reader takes every kind.
public static class ViewArchiveReader
{
    // Checks the central directory only, before anything is decompressed.
    public static void Validate(Stream archive, ViewTransferOptions options)
    {
        using var zip = Open(archive);
        _ = Inspect(zip, options);
    }

    public static ViewArchiveDocuments Read(Stream archive, ViewTransferOptions options)
    {
        using var zip = Open(archive);
        var (entries, ignored) = Inspect(zip, options);
        var files = new List<ViewArchiveFile>(entries.Count);
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (path, name, entry) in entries)
        {
            if (seen.TryGetValue(name.Identity, out var first))
            {
                files.Add(Failed(path, name, ErrorCodes.DuplicateFile, $"The file '{first}' already holds the configuration of {name.AnnotationKey}."));
                continue;
            }

            seen[name.Identity] = path;
            files.Add(ReadFile(path, name, entry, options.MaxDocumentBytes));
        }

        return new ViewArchiveDocuments(files, ignored);
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

    // The document files in path order, and the count of ignored files.
    private static (List<(string Path, ViewArchiveName Name, ZipArchiveEntry Entry)> Entries, int Ignored) Inspect(ZipArchive zip, ViewTransferOptions options)
    {
        if (zip.Entries.Count > options.MaxArchiveEntries)
        {
            throw new InvalidInputException(
                $"The archive has {zip.Entries.Count} entries, at most {options.MaxArchiveEntries} are allowed.",
                ErrorCodes.InvalidArchive);
        }

        var entries = new List<(string Path, ViewArchiveName Name, ZipArchiveEntry Entry)>();
        var ignored = 0;
        var declared = 0L;

        foreach (var entry in zip.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');

            if (path.EndsWith('/') || ViewArchiveNames.IsSkippedPath(path))
            {
                continue;
            }

            var fileName = path[(path.LastIndexOf('/') + 1)..];

            if (!ViewArchiveNames.TryParse(fileName, out var name)
                || name.Version.HasValue
                || name.Kind != ViewDocumentKind.Configuration)
            {
                ignored++;
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
        return (entries, ignored);
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
            return File(path, name) with { Content = ViewDocumentJson.Parse(stream) };
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            return Failed(path, name, ErrorCodes.InvalidDocument, $"The file is not a JSON object: {exception.Message}");
        }
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
}
