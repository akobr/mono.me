namespace _42.Platform.Storyteller.Transferring.Git;

// A git delta that turns the base into the target: copy the common prefix of both, insert the middle of the
// target, copy the common suffix. One commit changes one region of a document or of each tree on its path, so this
// small encoder is enough to keep consecutive versions cheap.
public static class GitDelta
{
    // A copy instruction moves at most 64 KiB; 0x10000 is written as size 0.
    private const int MaxCopySize = 0x10000;
    private const int MaxInsertSize = 0x7F;

    public static byte[] Create(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target)
    {
        var prefix = source.CommonPrefixLength(target);
        var limit = Math.Min(source.Length, target.Length) - prefix;
        var suffix = 0;

        while (suffix < limit && source[source.Length - 1 - suffix] == target[target.Length - 1 - suffix])
        {
            suffix++;
        }

        using var delta = new MemoryStream(32 + target.Length - prefix - suffix);
        WriteSize(delta, source.Length);
        WriteSize(delta, target.Length);
        WriteCopy(delta, 0, prefix);
        WriteInsert(delta, target[prefix..(target.Length - suffix)]);
        WriteCopy(delta, source.Length - suffix, suffix);
        return delta.ToArray();
    }

    private static void WriteSize(Stream stream, long size)
    {
        do
        {
            var value = (byte)(size & 0x7F);
            size >>= 7;
            stream.WriteByte(size > 0 ? (byte)(value | 0x80) : value);
        }
        while (size > 0);
    }

    private static void WriteCopy(Stream stream, long offset, int length)
    {
        Span<byte> instruction = stackalloc byte[8];

        while (length > 0)
        {
            var size = Math.Min(length, MaxCopySize);
            var command = (byte)0x80;
            var position = 1;

            for (var index = 0; index < 4; index++)
            {
                var value = (byte)((offset >> (8 * index)) & 0xFF);

                if (value != 0)
                {
                    command |= (byte)(1 << index);
                    instruction[position++] = value;
                }
            }

            if (size != MaxCopySize)
            {
                for (var index = 0; index < 3; index++)
                {
                    var value = (byte)((size >> (8 * index)) & 0xFF);

                    if (value != 0)
                    {
                        command |= (byte)(0x10 << index);
                        instruction[position++] = value;
                    }
                }
            }

            instruction[0] = command;
            stream.Write(instruction[..position]);
            offset += size;
            length -= size;
        }
    }

    private static void WriteInsert(Stream stream, ReadOnlySpan<byte> data)
    {
        while (data.Length > 0)
        {
            var size = Math.Min(data.Length, MaxInsertSize);
            stream.WriteByte((byte)size);
            stream.Write(data[..size]);
            data = data[size..];
        }
    }
}
