using System.IO;

namespace c0ded0c.Core.Hashing
{
    public interface IHashCalculator
    {
        int HashSize { get; }

        byte[] ComputeHash(byte[] buffer);

        byte[] ComputeHash(byte[] buffer, int offset, int count);

        byte[] ComputeHash(Stream inputStream);

        string ToStringRepresentation(byte[] hash);
    }
}