using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.KeyValues.ClientEncryption;

// Encrypts a value for the recipients of a project: AES-256-GCM content with a random key, the key wrapped
// with RSA-OAEP-256 for every recipient. The place ("{organization}/{project}/{view}/{lower-cased key}") goes
// into the protected header, so the server can refuse a document meant for another key.
public static class ClientSecretEncryptor
{
    private const int ContentKeySize = 32;
    private const int IvSize = 12;
    private const int TagSize = 16;

    public static string Encrypt(string value, IReadOnlyCollection<ClientEncryptionRecipientKey> recipients, string place, string contentType = "text/plain")
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(recipients);
        ArgumentException.ThrowIfNullOrWhiteSpace(place);

        if (recipients.Count == 0)
        {
            throw new ArgumentException("At least one recipient is required.", nameof(recipients));
        }

        var header = new JsonObject
        {
            ["enc"] = JweDocument.Encryption,
            [JweDocument.ContentTypeHeader] = contentType,
            [JweDocument.PlaceHeader] = place,
        };
        var protectedBase64 = System.Buffers.Text.Base64Url.EncodeToString(Encoding.UTF8.GetBytes(header.ToJsonString()));
        var contentKey = new byte[ContentKeySize];

        try
        {
            RandomNumberGenerator.Fill(contentKey);
            var iv = RandomNumberGenerator.GetBytes(IvSize);
            var plaintext = Encoding.UTF8.GetBytes(value);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(contentKey, TagSize))
            {
                aes.Encrypt(iv, plaintext, ciphertext, tag, Encoding.ASCII.GetBytes(protectedBase64));
            }

            var wrapped = new List<JweRecipient>(recipients.Count);

            foreach (var recipient in recipients)
            {
                using var rsa = recipient.PublicKey.ToRsa();
                wrapped.Add(new JweRecipient(recipient.Id, JweDocument.Algorithm, rsa.Encrypt(contentKey, RSAEncryptionPadding.OaepSHA256)));
            }

            return JweDocument.Serialize(header, wrapped, iv, ciphertext, tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }
}

public static class ClientSecretDecryptor
{
    public static string Decrypt(string jwe, string recipientId, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        var document = JweDocument.Parse(jwe);
        var recipient = document.Recipients.FirstOrDefault(entry => string.Equals(entry.KeyId, recipientId, StringComparison.Ordinal))
            ?? throw new CryptographicException($"The value was not encrypted for the recipient '{recipientId}'.");

        if (!string.Equals(recipient.Algorithm, JweDocument.Algorithm, StringComparison.Ordinal)
            || !string.Equals(document.Enc, JweDocument.Encryption, StringComparison.Ordinal))
        {
            throw new CryptographicException("The value uses an algorithm the decryptor does not support.");
        }

        var contentKey = privateKey.Decrypt(recipient.EncryptedKey, RSAEncryptionPadding.OaepSHA256);

        try
        {
            var plaintext = new byte[document.Ciphertext.Length];

            using (var aes = new AesGcm(contentKey, document.Tag.Length))
            {
                aes.Decrypt(document.Iv, document.Ciphertext, document.Tag, plaintext, Encoding.ASCII.GetBytes(document.ProtectedBase64));
            }

            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
        }
    }
}
