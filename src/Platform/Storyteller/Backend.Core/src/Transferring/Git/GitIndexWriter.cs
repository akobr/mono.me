using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace _42.Platform.Storyteller.Transferring.Git;

// Writes .git/index (version 2) for the checked-out working tree, so `git status` of the extracted folder is clean.
// Entries are files with their full paths ("templates/exe.template.json"), in the byte order of the path that git
// requires; directories have no entries of their own. Only size and modification time of the stat data are known;
// git compares the content once and refreshes the rest.
public static class GitIndexWriter
{
    private const uint RegularFileMode = 0x81A4; // 100644
    private const int FixedEntryLength = 62;

    public static byte[] Write(IEnumerable<GitIndexEntry> entries)
    {
        var ordered = entries.OrderBy(entry => entry.Path, GitPathComparer.Instance).ToList();
        using var index = new MemoryStream();
        Span<byte> fixedPart = stackalloc byte[FixedEntryLength];

        index.Write("DIRC"u8);
        WriteUInt32(index, 2);
        WriteUInt32(index, (uint)ordered.Count);

        foreach (var entry in ordered)
        {
            var path = Encoding.UTF8.GetBytes(entry.Path);
            var seconds = (uint)Math.Clamp(entry.ModifiedAt.ToUnixTimeSeconds(), 0, uint.MaxValue);

            fixedPart.Clear();
            BinaryPrimitives.WriteUInt32BigEndian(fixedPart[8..], seconds); // mtime; ctime, dev, ino, uid and gid stay 0
            BinaryPrimitives.WriteUInt32BigEndian(fixedPart[24..], RegularFileMode);
            BinaryPrimitives.WriteUInt32BigEndian(fixedPart[36..], (uint)entry.Size);
            entry.Id.CopyTo(fixedPart[40..]);
            BinaryPrimitives.WriteUInt16BigEndian(fixedPart[60..], (ushort)Math.Min(path.Length, 0xFFF));

            index.Write(fixedPart);
            index.Write(path);

            // 1 to 8 NUL bytes: the name stays terminated and the entry ends on a multiple of 8
            var padding = 8 - ((FixedEntryLength + path.Length) % 8);
            index.Write(new byte[padding]);
        }

        index.Write(SHA1.HashData(index.GetBuffer().AsSpan(0, (int)index.Length)));
        return index.ToArray();
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(word, value);
        stream.Write(word);
    }
}
