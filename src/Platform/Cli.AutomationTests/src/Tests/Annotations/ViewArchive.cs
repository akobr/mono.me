using System.Buffers.Binary;
using System.IO.Abstractions;
using System.IO.Compression;
using System.Text;

namespace _42.Platform.Cli.AutomationTests.Tests.Annotations;

// The zips of sform story export and story import (docs/Platform/Storyteller/export-import.md): the entries of an
// export (export.json, annotations/<key>.json, schemas/…, templates/…, configurations/<key>.conf[.<n>].json, or a git
// repository with the same working tree), and a zip the test writes for an import.
internal static class ViewArchive
{
    // The full names of the entries (with '/'), without the folder entries.
    public static IReadOnlyList<string> Entries(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        using var stream = fileSystem.File.OpenRead(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        return zip.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .Where(name => !name.EndsWith('/'))
            .ToList();
    }

    // The text of an entry; null when the zip has none of that name.
    public static string? ReadEntry(IFileSystem fileSystem, string path, string name)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        using var stream = fileSystem.File.OpenRead(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(candidate => candidate.FullName.Replace('\\', '/') == name);
        if (entry is null)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // Writes a zip of the files (entry name with '/', text written as UTF-8 without a BOM).
    public static void Write(IFileSystem fileSystem, string path, IReadOnlyDictionary<string, string> files)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(files);
        using var stream = fileSystem.File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, text) in files)
        {
            using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
            writer.Write(text);
        }
    }

    // The subjects (first lines of the messages) of every commit of the repository of a git export, read from the one
    // pack of the zip (.git/objects/pack/pack-<sha>.pack and its .idx), in no particular order. The index (version 2)
    // gives the offset of every object; a commit is a whole object (type 1) whose zlib stream starts after its header.
    // Deltas (the blobs and trees of later versions) are never commits, so they are passed over without reading them.
    public static IReadOnlyList<string> CommitSubjects(IFileSystem fileSystem, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        using var stream = fileSystem.File.OpenRead(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        static string NameOf(ZipArchiveEntry entry) => entry.FullName.Replace('\\', '/');
        var index = zip.Entries.SingleOrDefault(entry => NameOf(entry).StartsWith(".git/objects/pack/", StringComparison.Ordinal) && NameOf(entry).EndsWith(".idx", StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"The export '{path}' has no .git/objects/pack/*.idx: {string.Join(", ", zip.Entries.Select(NameOf))}.");
        var packName = NameOf(index)[..^".idx".Length] + ".pack";
        var pack = zip.Entries.SingleOrDefault(entry => NameOf(entry) == packName)
            ?? throw new InvalidOperationException($"The export '{path}' has {NameOf(index)} without its .pack.");

        var subjects = new List<string>();
        var packBytes = ReadAll(pack);
        foreach (var offset in ObjectOffsets(ReadAll(index)))
        {
            if (ReadCommit(packBytes, offset) is { } commit)
            {
                var start = commit.IndexOf("\n\n", StringComparison.Ordinal);
                var message = start < 0 ? string.Empty : commit[(start + 2)..];
                subjects.Add(message.Split('\n', 2)[0].Trim());
            }
        }

        return subjects;
    }

    // The offsets of the objects of a pack index of version 2: a magic, the version, 256 fan-out counts (the last is the
    // number of objects), the object names, their CRC-32s, their 4-byte offsets (the top bit set: an index into the 8-byte
    // offsets that follow).
    internal static IReadOnlyList<long> ObjectOffsets(byte[] index)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (index.Length < 8 + (256 * 4) || index[0] != 0xFF || index[1] != (byte)'t' || index[2] != (byte)'O' || index[3] != (byte)'c'
            || BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(4)) != 2)
        {
            throw new InvalidOperationException("The pack index is not of version 2.");
        }

        var count = (int)BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(8 + (255 * 4)));
        var names = 8 + (256 * 4);
        var offsets = names + (count * 20) + (count * 4);
        var large = offsets + (count * 4);
        var result = new List<long>(count);
        for (var position = 0; position < count; position++)
        {
            var offset = BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(offsets + (position * 4)));
            result.Add((offset & 0x80000000) == 0
                ? offset
                : (long)BinaryPrimitives.ReadUInt64BigEndian(index.AsSpan(large + ((int)(offset & 0x7FFFFFFF) * 8))));
        }

        return result;
    }

    // The text of the commit at the offset of the pack; null for an object of another type. An object starts with its
    // type (bits 4-6 of the first byte) and its inflated size (the low 4 bits, then 7 bits per byte while the top bit is
    // set); a whole object's zlib stream follows.
    internal static string? ReadCommit(byte[] pack, long offset)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var position = (int)offset;
        var current = pack[position++];
        var type = (current >> 4) & 0x7;
        var size = (long)(current & 0x0F);
        var shift = 4;
        while ((current & 0x80) != 0)
        {
            current = pack[position++];
            size |= (long)(current & 0x7F) << shift;
            shift += 7;
        }

        if (type != 1)
        {
            return null;
        }

        using var inflater = new ZLibStream(new MemoryStream(pack, position, pack.Length - position, writable: false), CompressionMode.Decompress);
        var buffer = new byte[size];
        inflater.ReadExactly(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        using var target = new MemoryStream();
        source.CopyTo(target);
        return target.ToArray();
    }
}
