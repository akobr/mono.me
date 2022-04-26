using System;
using System.IO;

namespace c0ded0c.Core.Hashing
{
    /// <summary>
    /// <para>
    /// MurmurHash3 x64 128-bit variant.
    /// </para>
    public class MurmurHash3 : IHashCalculator
    {
        private const ulong C1 = 0x87c37b91114253d5;
        private const ulong C2 = 0x4cf5ad432745937f;
        private const int HASH_BITS_SIZE = 128;
        private const int HASH_BYTES_SIZE = HASH_BITS_SIZE / 8;

        private readonly byte[] tail;
        private ulong h1;
        private ulong h2;
        private int tailLength;
        private ulong totalCount;

        /// <summary>
        /// Initialises a new instance of the <see cref="MurmurHash3"/> class.
        /// </summary>
        public MurmurHash3()
        {
            tail = new byte[HASH_BYTES_SIZE];
        }

        /// <summary>Gets the size, in bits, of the computed hash code.</summary>
        /// <returns>The size, in bits, of the computed hash code.</returns>
        public int HashSize => HASH_BITS_SIZE;

        /// <summary>Computes the hash value for the specified byte array.</summary>
        /// <param name="buffer">The input to compute the hash code for.</param>
        /// <returns>The computed hash code.</returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// <paramref name="buffer" /> is null.</exception>
        public byte[] ComputeHash(byte[] buffer)
        {
            HashRestart();
            HashStep(buffer, 0, buffer.Length);
            return HashFinal();
        }

        /// <summary>Computes the hash value for the specified region of the specified byte array.</summary>
        /// <param name="buffer">The input to compute the hash code for.</param>
        /// <param name="offset">The offset into the byte array from which to begin using data.</param>
        /// <param name="count">The number of bytes in the array to use as data.</param>
        /// <returns>The computed hash code.</returns>
        /// <exception cref="T:System.ArgumentException">
        /// <paramref name="count" /> is an invalid value.-or-<paramref name="buffer" /> length is invalid.</exception>
        /// <exception cref="T:System.ArgumentNullException">
        /// <paramref name="buffer" /> is null.</exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        /// <paramref name="offset" /> is out of range. This parameter requires a non-negative number.</exception>
        public byte[] ComputeHash(byte[] buffer, int offset, int count)
        {
            HashRestart();
            HashStep(buffer, offset, count);
            return HashFinal();
        }

        /// <summary>
        /// Computes the hash value for the specified stream.
        /// </summary>
        /// <param name="inputStream">The input stream to compute the hash code for.</param>
        /// <returns>The computed hash code.</returns>
        public byte[] ComputeHash(Stream inputStream)
        {
            // Default the buffer size to 4K.
            const int DEFAULT_BUFFER_LENGTH = 4096;
            byte[] buffer = new byte[DEFAULT_BUFFER_LENGTH + HASH_BYTES_SIZE];
            int bytesRead;

            do
            {
                Array.Clear(buffer, 0, HASH_BYTES_SIZE);
                bytesRead = inputStream.Read(buffer, HASH_BYTES_SIZE, DEFAULT_BUFFER_LENGTH);

                if (bytesRead > 0)
                {
                    HashStep(buffer, HASH_BYTES_SIZE, bytesRead);
                }
            }
            while (bytesRead > 0);

            return HashFinal();
        }

        public string ToStringRepresentation(byte[] hash)
        {
            if (hash == null)
            {
                throw new ArgumentNullException(nameof(hash));
            }

            return string.Concat(Array.ConvertAll(hash, x => x.ToString("x2")));
        }

        private void HashRestart()
        {
            h1 = h2 = totalCount = 0;
            tailLength = 0;
            Array.Clear(tail, 0, HASH_BYTES_SIZE);
        }

        private unsafe void HashStep(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, "offset must be >= 0");
            }

            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), count, "count must be >= 0");
            }

            if (offset + count > buffer.Length)
            {
                throw new ArgumentException($"offset ({offset}) + count ({count}) exceed buffer length ({buffer.Length})");
            }

            totalCount += (ulong)count;

            // usage of previous tail
            if (tailLength > 0)
            {
                if (offset < HASH_BYTES_SIZE)
                {
                    throw new ArgumentOutOfRangeException(nameof(offset), "tail can't be injected to buffer");
                }

                offset -= tailLength;
                count += tailLength;
                Array.Copy(tail, HASH_BYTES_SIZE - tailLength, buffer, offset, tailLength);
            }

            int nblocks = count / 16;

            // body of the hash
            fixed (byte* pbuffer = buffer)
            {
                byte* pinput = pbuffer + offset;
                ulong* body = (ulong*)pinput;

                ulong k1;
                ulong k2;

                for (int i = 0; i < nblocks; i++)
                {
                    k1 = body[i * 2];
                    k2 = body[(i * 2) + 1];

                    k1 *= C1;
                    k1 = (k1 << 31) | (k1 >> (64 - 31)); // ROTL64(k1, 31);
                    k1 *= C2;
                    h1 ^= k1;

                    h1 = (h1 << 27) | (h1 >> (64 - 27)); // ROTL64(h1, 27);
                    h1 += h2;
                    h1 = (h1 * 5) + 0x52dce729;

                    k2 *= C2;
                    k2 = (k2 << 33) | (k2 >> (64 - 33)); // ROTL64(k2, 33);
                    k2 *= C1;
                    h2 ^= k2;

                    h2 = (h2 << 31) | (h2 >> (64 - 31)); // ROTL64(h2, 31);
                    h2 += h1;
                    h2 = (h2 * 5) + 0x38495ab5;
                }
            }

            // store tail for next step
            Array.Clear(tail, 0, 16);
            int tailIndex = offset + (nblocks * HASH_BYTES_SIZE);
            int afterTailIndex = offset + count;

            if (tailIndex < afterTailIndex)
            {
                tailLength = afterTailIndex - tailIndex;
                Array.Copy(buffer, tailIndex, tail, HASH_BYTES_SIZE - tailLength, tailLength);
            }
            else
            {
                tailLength = 0;
            }
        }

        private unsafe byte[] HashFinal()
        {
            if (tailLength > 0)
            {
                // tail
                fixed (byte* ptailBuffer = tail)
                {
                    byte* ptail = ptailBuffer + (HASH_BYTES_SIZE - tailLength);
                    ulong k1 = 0;
                    ulong k2 = 0;

                    switch (totalCount & 15)
                    {
                        case 15:
                            k2 ^= (ulong)ptail[14] << 48;
                            goto case 14;
                        case 14:
                            k2 ^= (ulong)ptail[13] << 40;
                            goto case 13;
                        case 13:
                            k2 ^= (ulong)ptail[12] << 32;
                            goto case 12;
                        case 12:
                            k2 ^= (ulong)ptail[11] << 24;
                            goto case 11;
                        case 11:
                            k2 ^= (ulong)ptail[10] << 16;
                            goto case 10;
                        case 10:
                            k2 ^= (ulong)ptail[9] << 8;
                            goto case 9;
                        case 9:
                            k2 ^= ptail[8];
                            k2 *= C2;
                            k2 = (k2 << 33) | (k2 >> (64 - 33)); // ROTL64(k2, 33);
                            k2 *= C1;
                            h2 ^= k2;
                            goto case 8;
                        case 8:
                            k1 ^= (ulong)ptail[7] << 56;
                            goto case 7;
                        case 7:
                            k1 ^= (ulong)ptail[6] << 48;
                            goto case 6;
                        case 6:
                            k1 ^= (ulong)ptail[5] << 40;
                            goto case 5;
                        case 5:
                            k1 ^= (ulong)ptail[4] << 32;
                            goto case 4;
                        case 4:
                            k1 ^= (ulong)ptail[3] << 24;
                            goto case 3;
                        case 3:
                            k1 ^= (ulong)ptail[2] << 16;
                            goto case 2;
                        case 2:
                            k1 ^= (ulong)ptail[1] << 8;
                            goto case 1;
                        case 1:
                            k1 ^= ptail[0];
                            k1 *= C1;
                            k1 = (k1 << 31) | (k1 >> (64 - 31)); // ROTL64(k1, 31);
                            k1 *= C2;
                            h1 ^= k1;
                            break;
                    }
                }
            }

            // finalization
            h1 ^= totalCount;
            h2 ^= totalCount;

            h1 += h2;
            h2 += h1;

            h1 = FMix64(h1);
            h2 = FMix64(h2);

            h1 += h2;
            h2 += h1;

            var hash = new byte[HASH_BYTES_SIZE];

            fixed (byte* pret = hash)
            {
                var ulpret = (ulong*)pret;

                ulpret[0] = Reverse(h1);
                ulpret[1] = Reverse(h2);
            }

            return hash;
        }

        private static ulong FMix64(ulong k)
        {
            k ^= k >> 33;
            k *= 0xff51afd7ed558ccd;
            k ^= k >> 33;
            k *= 0xc4ceb9fe1a85ec53;
            k ^= k >> 33;
            return k;
        }

        private static ulong Reverse(ulong value)
        {
            return (value & 0x00000000000000FFUL) << 56 | (value & 0x000000000000FF00UL) << 40 |
                    (value & 0x0000000000FF0000UL) << 24 | (value & 0x00000000FF000000UL) << 8 |
                    (value & 0x000000FF00000000UL) >> 8 | (value & 0x0000FF0000000000UL) >> 24 |
                    (value & 0x00FF000000000000UL) >> 40 | (value & 0xFF00000000000000UL) >> 56;
        }
    }
}
