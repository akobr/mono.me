using System.Security.Cryptography;
using System.Text;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

// AES-GCM with the identity as associated data, as the contract asks: nonce | tag | ciphertext.
internal sealed class TestCacheProtector : IStorytellerCacheProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public TestCacheProtector(byte[]? key = null)
    {
        _key = key ?? RandomNumberGenerator.GetBytes(32);
    }

    public int ProtectCalls { get; private set; }

    public byte[] Protect(byte[] plaintext, string identity)
    {
        ProtectCalls++;

        var output = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = output.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(NonceSize + TagSize), output.AsSpan(NonceSize, TagSize), Encoding.UTF8.GetBytes(identity));
        return output;
    }

    public byte[] Unprotect(byte[] protectedData, string identity)
    {
        var plaintext = new byte[protectedData.Length - NonceSize - TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(
            protectedData.AsSpan(0, NonceSize),
            protectedData.AsSpan(NonceSize + TagSize),
            protectedData.AsSpan(NonceSize, TagSize),
            plaintext,
            Encoding.UTF8.GetBytes(identity));
        return plaintext;
    }
}
