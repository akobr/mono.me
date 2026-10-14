using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;

namespace _42.Platform.Storyteller.Transferring.Git;

// Writes a packfile (version 2) and its index (version 2) in memory. Objects can be stored as OFS_DELTA against
// an earlier object of the pack; a delta chain is cut after MaxDeltaDepth deltas so reading stays fast.
public sealed class GitPackWriter
{
    public const int MaxDeltaDepth = 49;

    private const int HeaderLength = 12;

    private readonly MemoryStream _pack = new();
    private readonly Dictionary<string, PackedObject> _objects = new(StringComparer.Ordinal);

    public GitPackWriter()
    {
        // "PACK", version 2, the object count is filled in by Complete
        _pack.Write("PACK"u8);
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, 2);
        _pack.Write(header);
    }

    public int Count => _objects.Count;

    public bool Contains(byte[] id) => _objects.ContainsKey(GitObjectId.ToHex(id));

    // Stores the object once; a base that is in the pack and not too deep makes it a delta when that is smaller.
    public void Add(GitObjectType type, byte[] id, byte[] content, byte[]? baseId = null, byte[]? baseContent = null)
    {
        var key = GitObjectId.ToHex(id);

        if (_objects.ContainsKey(key))
        {
            return;
        }

        var offset = _pack.Position;
        var depth = 0;
        byte[] entry;

        if (baseId is not null
            && baseContent is not null
            && _objects.TryGetValue(GitObjectId.ToHex(baseId), out var @base)
            && @base.Depth < MaxDeltaDepth
            && GitDelta.Create(baseContent, content) is { } delta
            && delta.Length * 2 < content.Length)
        {
            entry = CreateEntry(GitObjectType.OfsDelta, delta, offset - @base.Offset);
            depth = @base.Depth + 1;
        }
        else
        {
            entry = CreateEntry(type, content, 0);
        }

        _pack.Write(entry);
        _objects[key] = new PackedObject(id, offset, Crc32.Compute(entry), depth);
    }

    // The pack with its checksum, the index, and the name of both files (pack-<checksum>).
    public GitPack Complete()
    {
        var count = _objects.Count;
        var buffer = _pack.GetBuffer();
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(8, 4), (uint)count);

        var checksum = SHA1.HashData(buffer.AsSpan(0, (int)_pack.Length));
        var pack = new byte[_pack.Length + checksum.Length];
        buffer.AsSpan(0, (int)_pack.Length).CopyTo(pack);
        checksum.CopyTo(pack, _pack.Length);

        return new GitPack($"pack-{GitObjectId.ToHex(checksum)}", pack, CreateIndex(checksum));
    }

    private byte[] CreateIndex(byte[] packChecksum)
    {
        var objects = _objects.Values
            .OrderBy(item => item.Id, ByteArrayComparer.Instance)
            .ToList();

        using var index = new MemoryStream(8 + (256 * 4) + (objects.Count * 28) + 40);
        Span<byte> word = stackalloc byte[4];

        index.Write([0xFF, (byte)'t', (byte)'O', (byte)'c']);
        WriteUInt32(index, word, 2);

        // fan-out: objects whose first byte is at most i
        var cumulative = 0u;
        var position = 0;

        for (var value = 0; value < 256; value++)
        {
            while (position < objects.Count && objects[position].Id[0] == value)
            {
                cumulative++;
                position++;
            }

            WriteUInt32(index, word, cumulative);
        }

        foreach (var item in objects)
        {
            index.Write(item.Id);
        }

        foreach (var item in objects)
        {
            WriteUInt32(index, word, item.Crc);
        }

        foreach (var item in objects)
        {
            if (item.Offset > int.MaxValue)
            {
                throw new InvalidOperationException("The packfile is larger than 2 GiB.");
            }

            WriteUInt32(index, word, (uint)item.Offset);
        }

        index.Write(packChecksum);
        index.Write(SHA1.HashData(index.GetBuffer().AsSpan(0, (int)index.Length)));
        return index.ToArray();
    }

    private static byte[] CreateEntry(GitObjectType type, byte[] data, long baseDistance)
    {
        using var entry = new MemoryStream(data.Length / 2 + 32);

        // type and size: 3 bits of type and 4 bits of size, then 7 bits of size per byte
        var size = (long)data.Length;
        var current = (byte)(((int)type << 4) | (int)(size & 0x0F));
        size >>= 4;

        while (size > 0)
        {
            entry.WriteByte((byte)(current | 0x80));
            current = (byte)(size & 0x7F);
            size >>= 7;
        }

        entry.WriteByte(current);

        if (type == GitObjectType.OfsDelta)
        {
            WriteBaseDistance(entry, baseDistance);
        }

        using (var zlib = new ZLibStream(entry, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return entry.ToArray();
    }

    // The distance back to the base: big-endian groups of 7 bits, each continued group adding one (git's encoding).
    private static void WriteBaseDistance(Stream stream, long distance)
    {
        Span<byte> buffer = stackalloc byte[10];
        var position = buffer.Length - 1;
        buffer[position] = (byte)(distance & 0x7F);

        while ((distance >>= 7) > 0)
        {
            distance--;
            buffer[--position] = (byte)(0x80 | (distance & 0x7F));
        }

        stream.Write(buffer[position..]);
    }

    private static void WriteUInt32(Stream stream, Span<byte> word, uint value)
    {
        BinaryPrimitives.WriteUInt32BigEndian(word, value);
        stream.Write(word);
    }

    private sealed record class PackedObject(byte[] Id, long Offset, uint Crc, int Depth);

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public static readonly ByteArrayComparer Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}
