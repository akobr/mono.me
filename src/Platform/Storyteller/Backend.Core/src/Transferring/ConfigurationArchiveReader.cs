using System.Globalization;
using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Transferring;

// Reads the configuration files of a zip, in subfolders too, so a zipped folder or repository works.
// Version files, export.json and other files are ignored; .git, __MACOSX and ._* entries are skipped.
public static class ConfigurationArchiveReader
{
    // Checks the central directory only, before anything is decompressed.
    public static void Validate(Stream archive, ConfigurationTransferOptions options)
    {
        using var zip = Open(archive);
        _ = Inspect(zip, options);
    }

    public static ConfigurationArchiveContent Read(Stream archive, ConfigurationTransferOptions options)
    {
        using var zip = Open(archive);
        var (entries, ignored) = Inspect(zip, options);
        var files = new List<ConfigurationArchiveFile>(entries.Count);
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (path, key, entry) in entries)
        {
            var keyText = key.ToString();

            if (seen.TryGetValue(keyText, out var first))
            {
                files.Add(Failed(path, key, ErrorCodes.DuplicateFile, $"The file '{first}' already holds the configuration of {keyText}."));
                continue;
            }

            seen[keyText] = path;
            files.Add(ReadFile(path, key, entry, options.MaxDocumentBytes));
        }

        return new ConfigurationArchiveContent(files, ignored);
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

    // The configuration files in path order, and the count of ignored files.
    private static (List<(string Path, AnnotationKey Key, ZipArchiveEntry Entry)> Entries, int Ignored) Inspect(ZipArchive zip, ConfigurationTransferOptions options)
    {
        if (zip.Entries.Count > options.MaxArchiveEntries)
        {
            throw new InvalidInputException(
                $"The archive has {zip.Entries.Count} entries, at most {options.MaxArchiveEntries} are allowed.",
                ErrorCodes.InvalidArchive);
        }

        var entries = new List<(string Path, AnnotationKey Key, ZipArchiveEntry Entry)>();
        var ignored = 0;
        var declared = 0L;

        foreach (var entry in zip.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');

            if (path.EndsWith('/') || IsSkipped(path))
            {
                continue;
            }

            var fileName = path[(path.LastIndexOf('/') + 1)..];

            if (!ConfigurationFileNames.TryParse(fileName, out var key, out var version) || version.HasValue)
            {
                ignored++;
                continue;
            }

            declared += entry.Length;

            if (declared > options.MaxExtractedBytes)
            {
                throw new InvalidInputException(
                    $"The configuration files of the archive are larger than {options.MaxExtractedBytes.ToString(CultureInfo.InvariantCulture)} bytes uncompressed.",
                    ErrorCodes.InvalidArchive);
            }

            entries.Add((path, key, entry));
        }

        entries.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return (entries, ignored);
    }

    private static bool IsSkipped(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return segments.Length == 0
               || segments.Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase))
               || segments[0].Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)
               || segments[^1].StartsWith("._", StringComparison.Ordinal);
    }

    private static ConfigurationArchiveFile ReadFile(string path, AnnotationKey key, ZipArchiveEntry entry, int maxBytes)
    {
        if (entry.Length > maxBytes)
        {
            return TooLarge(path, key, maxBytes);
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
                    return TooLarge(path, key, maxBytes);
                }
            }

            bytes = buffer.ToArray();
        }
        catch (InvalidDataException exception)
        {
            return Failed(path, key, ErrorCodes.InvalidDocument, $"The file cannot be decompressed: {exception.Message}");
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            return new ConfigurationArchiveFile { Path = path, Key = key, Content = ConfigurationDocumentJson.Parse(stream) };
        }
        catch (Exception exception) when (exception is JsonException or DecoderFallbackException)
        {
            return Failed(path, key, ErrorCodes.InvalidDocument, $"The file is not a JSON object: {exception.Message}");
        }
    }

    private static ConfigurationArchiveFile TooLarge(string path, AnnotationKey key, int maxBytes)
    {
        return Failed(path, key, ErrorCodes.InvalidDocument, $"The file is larger than {maxBytes.ToString(CultureInfo.InvariantCulture)} bytes.");
    }

    private static ConfigurationArchiveFile Failed(string path, AnnotationKey key, string errorCode, string message)
    {
        return new ConfigurationArchiveFile { Path = path, Key = key, ErrorCode = errorCode, Message = message };
    }
}
