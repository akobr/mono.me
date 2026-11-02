using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// A zip of a view's documents for Modeling's Import, built in memory (nothing touches the disk): one entry per file with
// UTF-8 text, deflated, which the mock's reader (mock/zip.ts: stored and deflated entries, no zip64) and the API both
// read. The import recognizes a file by its name, not its folder (mock/archive-names.ts, the API's ViewArchiveNames):
// annotations/<key>.json, configurations/<key>.conf.json, schemas/<key>.schema.json, and a name that is no document
// (notes.txt) counts as an ignored file. ToPayload gives the file the Import's input takes, under the name that the
// result's banner and the toaster then show.
public sealed class ViewArchiveBuilder
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly List<(string Path, string Text)> entries = [];

    // A file of the archive with the text as is (also text that is no JSON, e.g. "{ not json").
    public ViewArchiveBuilder Add(string path, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);

        entries.Add((path, text));
        return this;
    }

    // annotations/<key>.json: the editable members of an annotation, e.g. new { Title = "…" }.
    public ViewArchiveBuilder AddAnnotation(string key, object document)
    {
        return AddJson($"annotations/{key}.json", document);
    }

    // configurations/<key>.conf.json: the own configuration of the annotation.
    public ViewArchiveBuilder AddConfiguration(string key, object document)
    {
        return AddJson($"configurations/{key}.conf.json", document);
    }

    // schemas/<key>.schema.json: the annotation schema of the key (a type schema is schemas/<type>.schema.json).
    public ViewArchiveBuilder AddAnnotationSchema(string key, object document)
    {
        return AddJson($"schemas/{key}.schema.json", document);
    }

    // A file with the document as JSON: a string is taken as the JSON text, anything else is serialized.
    public ViewArchiveBuilder AddJson(string path, object document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Add(path, document as string ?? JsonSerializer.Serialize(document));
    }

    // The zip's bytes. The archive is disposed before the bytes are read: its dispose writes the central directory.
    public byte[] ToArray()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in entries)
            {
                var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
                using var writer = new StreamWriter(entry.Open(), Utf8);
                writer.Write(text);
            }
        }

        return stream.ToArray();
    }

    // The archive as the file of the Import's input, named fileName (e.g. at-<run>-import.zip).
    public FilePayload ToPayload(string fileName)
    {
        return Payload(fileName, ToArray());
    }

    // A file named like a zip that is none, e.g. the text "not a zip": the input's accept filter does not apply to a
    // file set by the test, so the server gets it and refuses it.
    public static FilePayload NotAZip(string fileName, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Payload(fileName, Utf8.GetBytes(text));
    }

    // Bytes of a zip, e.g. a downloaded export, as the file of the Import's input.
    public static FilePayload Payload(string fileName, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        return new FilePayload { Name = fileName, MimeType = "application/zip", Buffer = content };
    }
}
