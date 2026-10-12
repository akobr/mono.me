namespace _42.Platform.Storyteller.KeyValues.Protection;

// The value of one version as it is stored: plaintext, an envelope, or a client-encrypted document.
public record class StoredValue
{
    public required KeyValueProtection Protection { get; init; }

    public string? ContentType { get; init; }

    // Public: the value. Hyok: the client-encrypted document.
    public string? Value { get; init; }

    // Standard, Hsm, Byok.
    public EncryptedValue? Encrypted { get; init; }

    // Hyok: the recipient ids found in the document, for display.
    public IReadOnlyList<string>? Recipients { get; init; }

    // Plaintext length in UTF-8 bytes (Public, Standard, Hsm, Byok) or document length (Hyok).
    public int Length { get; init; }
}

// An envelope: AES-256-GCM ciphertext and the data key wrapped by a key-encryption key.
public record class EncryptedValue
{
    public int Format { get; init; } = 1;

    // The key-encryption key with its version, e.g. https://v.vault.azure.net/keys/kek-…/3f2…
    public required string KeyId { get; init; }

    // The wrap algorithm, RSA-OAEP-256.
    public required string Algorithm { get; init; }

    // base64url of the wrapped 256-bit data key.
    public required string WrappedKey { get; init; }

    // base64url, 96 bits.
    public required string Nonce { get; init; }

    // base64url, the ciphertext followed by the 128-bit tag.
    public required string Ciphertext { get; init; }
}
