namespace _42.Platform.Storyteller.Accessing.Model;

public record CertificateAuthorityRecord(
    byte[] CertificateData,
    byte[]? Pkcs12Data,
    string? KeyVaultKeyIdentifier,
    string Version);
