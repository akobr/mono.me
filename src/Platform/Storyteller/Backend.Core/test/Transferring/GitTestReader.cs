using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using _42.Platform.Storyteller.Transferring.Git;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

// An independent reader of the repository in an exported zip: it checks the pack and its index while it reads
// them, resolves deltas, and walks the history from HEAD.
internal sealed class GitTestReader
{
    private readonly Dictionary<string, (string Type, byte[] Content)> _objects = new(StringComparer.Ordinal);

    public GitTestReader(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        Files = archive.Entries.ToDictionary(entry => entry.FullName, entry => ReadAll(entry), StringComparer.Ordinal);

        Head = Encoding.ASCII.GetString(Files[".git/HEAD"]);
        Branch = Files.TryGetValue(".git/refs/heads/main", out var branch) ? Encoding.ASCII.GetString(branch).Trim() : null;

        var packName = Files.Keys.SingleOrDefault(name => name.StartsWith(".git/objects/pack/", StringComparison.Ordinal) && name.EndsWith(".pack", StringComparison.Ordinal));

        if (packName is not null)
        {
            ReadPack(Files[packName], Files[packName[..^".pack".Length] + ".idx"], packName);
        }
    }

    public Dictionary<string, byte[]> Files { get; }

    public string Head { get; }

    public string? Branch { get; }

    public int ObjectCount => _objects.Count;

    public int DeltaCount => DeltaIds.Count;

    // The objects stored as a delta against another object of the pack.
    public HashSet<string> DeltaIds { get; } = new(StringComparer.Ordinal);

    public (string Type, byte[] Content) GetObject(string id) => _objects[id];

    // Commits from the oldest to HEAD.
    public IReadOnlyList<GitTestCommit> GetHistory()
    {
        var commits = new List<GitTestCommit>();
        var id = Branch;

        while (id is not null)
        {
            var (type, content) = _objects[id];
            type.ShouldBe("commit");
            var text = Encoding.UTF8.GetString(content);
            var (headers, message) = SplitOnce(text, "\n\n");
            var lines = headers.Split('\n');

            var tree = lines.Single(line => line.StartsWith("tree ", StringComparison.Ordinal))[5..];
            var parent = lines.SingleOrDefault(line => line.StartsWith("parent ", StringComparison.Ordinal))?[7..];
            var author = lines.Single(line => line.StartsWith("author ", StringComparison.Ordinal))[7..];
            var committer = lines.Single(line => line.StartsWith("committer ", StringComparison.Ordinal))[10..];

            commits.Add(new GitTestCommit(id, author, committer, message, tree, ReadTree(tree)));
            id = parent;
        }

        commits.Reverse();
        return commits;
    }

    // Every file below the tree by its full path, walking the subtrees.
    public Dictionary<string, string> ReadTree(string id)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        AddFiles(id, string.Empty, files);
        return files;
    }

    // The entries of one tree: files (100644) and subtrees (40000), ordered like git, where a subtree compares as
    // its name followed by '/'.
    public IReadOnlyList<GitTestTreeEntry> ReadTreeEntries(string id)
    {
        var (type, content) = _objects[id];
        type.ShouldBe("tree");
        var entries = new List<GitTestTreeEntry>();
        var position = 0;

        while (position < content.Length)
        {
            var space = Array.IndexOf(content, (byte)' ', position);
            var mode = Encoding.ASCII.GetString(content, position, space - position);
            mode.ShouldBeOneOf("100644", "40000");
            var zero = Array.IndexOf(content, (byte)0, space);
            var name = Encoding.UTF8.GetString(content, space + 1, zero - space - 1);
            name.ShouldNotBeEmpty();
            name.ShouldNotContain("/");
            var entryId = Convert.ToHexStringLower(content.AsSpan(zero + 1, 20));
            _objects[entryId].Type.ShouldBe(mode == "40000" ? "tree" : "blob");
            entries.Add(new GitTestTreeEntry(mode, name, entryId));
            position = zero + 21;
        }

        entries.Select(entry => entry.Name).ShouldBeUnique();
        var sortNames = entries.Select(entry => entry.IsTree ? $"{entry.Name}/" : entry.Name).ToList();
        sortNames.ShouldBe(sortNames.OrderBy(name => Encoding.UTF8.GetBytes(name), ByteOrder.Instance).ToList());
        return entries;
    }

    private void AddFiles(string id, string prefix, Dictionary<string, string> files)
    {
        foreach (var entry in ReadTreeEntries(id))
        {
            if (entry.IsTree)
            {
                // git never stores an empty directory
                ReadTreeEntries(entry.Id).ShouldNotBeEmpty();
                AddFiles(entry.Id, $"{prefix}{entry.Name}/", files);
            }
            else
            {
                files.Add(prefix + entry.Name, Encoding.UTF8.GetString(_objects[entry.Id].Content));
            }
        }
    }

    private void ReadPack(byte[] pack, byte[] index, string packName)
    {
        // pack: "PACK", version 2, count, entries, SHA-1 of everything before
        Encoding.ASCII.GetString(pack, 0, 4).ShouldBe("PACK");
        BinaryPrimitives.ReadUInt32BigEndian(pack.AsSpan(4)).ShouldBe(2u);
        var count = (int)BinaryPrimitives.ReadUInt32BigEndian(pack.AsSpan(8));
        var packChecksum = pack.AsSpan(pack.Length - 20).ToArray();
        SHA1.HashData(pack.AsSpan(0, pack.Length - 20)).ShouldBe(packChecksum);
        packName.ShouldEndWith($"pack-{Convert.ToHexStringLower(packChecksum)}.pack");

        // index: magic, version 2, fan-out, ids, CRC-32, offsets, pack checksum, own checksum
        index.AsSpan(0, 4).ToArray().ShouldBe(new byte[] { 0xFF, (byte)'t', (byte)'O', (byte)'c' });
        BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(4)).ShouldBe(2u);
        var fanout = Enumerable.Range(0, 256).Select(i => BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(8 + (i * 4)))).ToArray();
        fanout[255].ShouldBe((uint)count);
        fanout.ShouldBe(fanout.Order().ToArray());

        var idsStart = 8 + (256 * 4);
        var crcStart = idsStart + (count * 20);
        var offsetsStart = crcStart + (count * 4);
        index.AsSpan(offsetsStart + (count * 4), 20).ToArray().ShouldBe(packChecksum);
        SHA1.HashData(index.AsSpan(0, index.Length - 20)).ShouldBe(index.AsSpan(index.Length - 20).ToArray());
        index.Length.ShouldBe(offsetsStart + (count * 4) + 40);

        var entries = Enumerable.Range(0, count)
            .Select(i => (
                Id: index.AsSpan(idsStart + (i * 20), 20).ToArray(),
                Crc: BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(crcStart + (i * 4))),
                Offset: (long)BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(offsetsStart + (i * 4)))))
            .ToList();

        for (var i = 0; i < 256; i++)
        {
            entries.Count(entry => entry.Id[0] <= i).ShouldBe((int)fanout[i]);
        }

        entries.Select(entry => entry.Id).ShouldBe(entries.Select(entry => entry.Id).OrderBy(id => id, ByteOrder.Instance).ToList());

        var offsets = entries.Select(entry => entry.Offset).Order().ToList();
        var ends = offsets.Zip(offsets.Skip(1).Append(pack.Length - 20)).ToDictionary(pair => pair.First, pair => pair.Second);
        var resolved = new Dictionary<long, (string Type, byte[] Content)>();

        foreach (var entry in entries)
        {
            Crc32.Compute(pack.AsSpan((int)entry.Offset, (int)(ends[entry.Offset] - entry.Offset))).ShouldBe(entry.Crc);
            var (type, content) = Resolve(pack, entry.Offset, ends, resolved);
            var id = Convert.ToHexStringLower(SHA1.HashData([.. Encoding.ASCII.GetBytes($"{type} {content.Length}\0"), .. content]));
            id.ShouldBe(Convert.ToHexStringLower(entry.Id));
            _objects[id] = (type, content);

            if (((pack[entry.Offset] >> 4) & 7) == 6)
            {
                DeltaIds.Add(id);
            }
        }
    }

    private (string Type, byte[] Content) Resolve(byte[] pack, long offset, Dictionary<long, long> ends, Dictionary<long, (string Type, byte[] Content)> resolved)
    {
        if (resolved.TryGetValue(offset, out var known))
        {
            return known;
        }

        var position = (int)offset;
        var current = pack[position++];
        var type = (current >> 4) & 7;
        var size = (long)(current & 0x0F);
        var shift = 4;

        while ((current & 0x80) != 0)
        {
            current = pack[position++];
            size |= (long)(current & 0x7F) << shift;
            shift += 7;
        }

        long baseOffset = 0;

        if (type == 6)
        {
            current = pack[position++];
            var distance = (long)(current & 0x7F);

            while ((current & 0x80) != 0)
            {
                distance++;
                current = pack[position++];
                distance = (distance << 7) + (current & 0x7F);
            }

            baseOffset = offset - distance;
        }

        using var zlib = new ZLibStream(new MemoryStream(pack, position, (int)(ends[offset] - position)), CompressionMode.Decompress);
        using var data = new MemoryStream();
        zlib.CopyTo(data);
        data.Length.ShouldBe(size);

        (string Type, byte[] Content) result;

        if (type == 6)
        {
            var @base = Resolve(pack, baseOffset, ends, resolved);
            result = (@base.Type, ApplyDelta(@base.Content, data.ToArray()));
        }
        else
        {
            result = (type switch { 1 => "commit", 2 => "tree", 3 => "blob", _ => throw new InvalidDataException($"type {type}") }, data.ToArray());
        }

        resolved[offset] = result;
        return result;
    }

    public static byte[] ApplyDelta(byte[] source, byte[] delta)
    {
        var position = 0;
        ReadSize(delta, ref position).ShouldBe(source.Length);
        var targetSize = ReadSize(delta, ref position);
        using var target = new MemoryStream();

        while (position < delta.Length)
        {
            var command = delta[position++];

            if ((command & 0x80) != 0)
            {
                var copyOffset = 0;
                var copySize = 0;

                for (var i = 0; i < 4; i++)
                {
                    if ((command & (1 << i)) != 0)
                    {
                        copyOffset |= delta[position++] << (8 * i);
                    }
                }

                for (var i = 0; i < 3; i++)
                {
                    if ((command & (0x10 << i)) != 0)
                    {
                        copySize |= delta[position++] << (8 * i);
                    }
                }

                if (copySize == 0)
                {
                    copySize = 0x10000;
                }

                target.Write(source, copyOffset, copySize);
            }
            else
            {
                command.ShouldNotBe((byte)0);
                target.Write(delta, position, command);
                position += command;
            }
        }

        target.Length.ShouldBe(targetSize);
        return target.ToArray();
    }

    private static int ReadSize(byte[] data, ref int position)
    {
        var size = 0;
        var shift = 0;
        byte current;

        do
        {
            current = data[position++];
            size |= (current & 0x7F) << shift;
            shift += 7;
        }
        while ((current & 0x80) != 0);

        return size;
    }

    private static (string Before, string After) SplitOnce(string text, string separator)
    {
        var index = text.IndexOf(separator, StringComparison.Ordinal);
        return (text[..index], text[(index + separator.Length)..]);
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private sealed class ByteOrder : IComparer<byte[]>
    {
        public static readonly ByteOrder Instance = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }
}

// Files: every file of the commit's tree by its full path.
internal sealed record class GitTestCommit(string Id, string Author, string Committer, string Message, string Tree, Dictionary<string, string> Files);
