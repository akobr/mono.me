using System.Buffers;
using System.Text;

namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// Decodes a stream that mixes UTF-8 with a single- or double-byte code page: each valid UTF-8 sequence as UTF-8, each
// other run of bytes in the code page; a sequence cut at the end of a chunk waits for the next one (Decode with flush at
// the end of the stream).
internal sealed class ConsoleOutputDecoder
{
    private readonly Encoding fallback;
    private byte[] pending = [];

    public ConsoleOutputDecoder(Encoding fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);
        this.fallback = fallback;
    }

    public string Decode(ReadOnlySpan<byte> bytes, bool flush = false)
    {
        byte[]? joined = null;
        var input = bytes;
        if (pending.Length > 0)
        {
            joined = new byte[pending.Length + bytes.Length];
            pending.CopyTo(joined, 0);
            bytes.CopyTo(joined.AsSpan(pending.Length));
            input = joined;
            pending = [];
        }

        var text = new StringBuilder(input.Length);
        var invalidStart = -1;
        var index = 0;
        while (index < input.Length)
        {
            if (input[index] < 0x80)
            {
                FlushInvalid(input, index);
                text.Append((char)input[index]);
                index++;
                continue;
            }

            var status = Rune.DecodeFromUtf8(input[index..], out var rune, out var consumed);
            if (status == OperationStatus.Done)
            {
                FlushInvalid(input, index);
                text.Append(rune.ToString());
                index += consumed;
            }
            else if (status == OperationStatus.NeedMoreData && !flush)
            {
                FlushInvalid(input, index);
                pending = input[index..].ToArray();
                return text.ToString();
            }
            else
            {
                if (invalidStart < 0)
                {
                    invalidStart = index;
                }

                index += Math.Max(1, consumed);
            }
        }

        FlushInvalid(input, index);
        return text.ToString();

        void FlushInvalid(ReadOnlySpan<byte> source, int end)
        {
            if (invalidStart >= 0)
            {
                text.Append(fallback.GetString(source[invalidStart..end]));
                invalidStart = -1;
            }
        }
    }
}
