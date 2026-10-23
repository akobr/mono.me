using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using _42.Platform.Storyteller.Sdk.Configuring.Reading;

namespace _42.Platform.Storyteller.Sdk.Configuring.Caching;

// The identity of a document plus the fingerprint of the credential that read it, so an entry written under one
// principal is never applied under another.
internal sealed record CacheIdentity
{
    // The first 32 hex characters of SHA-256.
    private const int FileNameLength = 32;

    public CacheIdentity(DocumentIdentity document, string credentialFingerprint)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(credentialFingerprint);

        Document = document;
        CredentialFingerprint = credentialFingerprint;

        // A JSON array: no value can fake a separator.
        Text = JsonSerializer.Serialize(new[]
        {
            document.Endpoint,
            document.Organization,
            document.Project,
            document.View,
            document.AnnotationKey,
            DocumentIdentity.ToText(document.Mode),
            credentialFingerprint,
        });
        FileName = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Text)))[..FileNameLength];
    }

    public DocumentIdentity Document { get; }

    public string CredentialFingerprint { get; }

    // Stored in the entry and checked on read; the purpose of a cache protector.
    public string Text { get; }

    public string FileName { get; }

    public override string ToString()
    {
        return Document.ToString();
    }
}
