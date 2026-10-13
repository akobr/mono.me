using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using _42.Platform.Storyteller.KeyValues.ClientEncryption;

namespace _42.Platform.Storyteller.Sdk.ClientSideEncryption;

// Client-side encryption of key/value secrets (the Hyok level): the value is encrypted here, for the recipients a project
// registered, and the platform stores a document it cannot read. The format is JWE General JSON Serialization with
// A256GCM and RSA-OAEP-256, the same the TypeScript SDK (./hyok) and the CLI produce and read.
public static class KeyValueClientEncryption
{
    // The protected header carries the place the value is written to, so a document cannot be moved to another key.
    public static string GetPlace(string organization, string project, string view, string key)
    {
        if (string.IsNullOrWhiteSpace(organization) || string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(view) || string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("The organization, project, view and key are required.");
        }

        return $"{organization}/{project}/{view}/{key.Trim().ToLowerInvariant()}";
    }

    // Encrypts a value for the enabled recipients of the project, as GET …/key-values/recipients returns them.
    public static string Encrypt(string value, IEnumerable<ClientEncryptionRecipient> recipients, string organization, string project, string view, string key, string contentType = "text/plain")
    {
        if (recipients is null)
        {
            throw new ArgumentNullException(nameof(recipients));
        }

        var keys = recipients
            .Where(recipient => recipient.IsEnabled)
            .Select(recipient => new ClientEncryptionRecipientKey(recipient.Id, new RsaPublicJwk(recipient.PublicKey.N, recipient.PublicKey.E)))
            .ToList();

        if (keys.Count == 0)
        {
            throw new ArgumentException("The project has no enabled recipient to encrypt for.", nameof(recipients));
        }

        return ClientSecretEncryptor.Encrypt(value, keys, GetPlace(organization, project, view, key), contentType);
    }

    // A KeyValueWrite for PUT …/key-values/{key}: the Hyok level and the encrypted document.
    public static KeyValueWrite CreateWrite(string value, IEnumerable<ClientEncryptionRecipient> recipients, string organization, string project, string view, string key, string contentType = "text/plain", long? expectedVersion = null)
    {
        return new KeyValueWrite
        {
            Protection = KeyValueWriteProtection.Hyok,
            Value = Encrypt(value, recipients, organization, project, view, key, contentType),
            ContentType = contentType,
            ExpectedVersion = expectedVersion,
        };
    }

    // Decrypts a document returned by a reveal, a secret version, an export or a binding, with one recipient's private key.
    public static string Decrypt(string document, string recipientId, RSA privateKey)
    {
        return ClientSecretDecryptor.Decrypt(document, recipientId, privateKey);
    }

    // The place a document was encrypted for, to check it against the key it was read from.
    public static string? GetPlace(string document)
    {
        return JweDocument.Parse(document).Place;
    }
}
