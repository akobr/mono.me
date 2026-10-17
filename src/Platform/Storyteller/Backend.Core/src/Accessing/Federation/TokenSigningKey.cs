namespace _42.Platform.Storyteller.Accessing.Federation;

// The private key Storyteller signs its tokens with. Kid is the RFC 7638 thumbprint of the public key.
public sealed record TokenSigningKey(
    string Kid,
    string Algorithm,
    byte[] PrivateKeyPkcs8,
    DateTimeOffset CreatedAt);
