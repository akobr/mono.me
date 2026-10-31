using System.Buffers.Binary;
using System.Text;

namespace _42.Testing.UI.Automation.Runtime.Data;

// Hashes that are the same in every process (string.GetHashCode is randomized per process): FNV-1a over UTF-8, with
// the MurmurHash3 finalizer to spread combined seeds.
public static class StableHash
{
    private const uint OffsetBasis = 2166136261;
    private const uint Prime = 16777619;

    public static uint Fnv1a32(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Append(OffsetBasis, Encoding.UTF8.GetBytes(value));
    }

    // A non-negative seed from the session seed, the test id and the attempt; the same for the same three values.
    public static int Combine(int seed, string value, int attempt)
    {
        ArgumentNullException.ThrowIfNull(value);

        Span<byte> number = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(number, seed);
        var hash = Append(OffsetBasis, number);
        hash = Append(hash, Encoding.UTF8.GetBytes(value));
        BinaryPrimitives.WriteInt32LittleEndian(number, attempt);
        hash = Append(hash, number);
        return (int)(Mix(hash) & int.MaxValue);
    }

    private static uint Append(uint hash, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            hash ^= value;
            hash = unchecked(hash * Prime);
        }

        return hash;
    }

    private static uint Mix(uint hash)
    {
        unchecked
        {
            hash ^= hash >> 16;
            hash *= 0x85ebca6b;
            hash ^= hash >> 13;
            hash *= 0xc2b2ae35;
            hash ^= hash >> 16;
            return hash;
        }
    }
}
