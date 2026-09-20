using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface ICertificateAuthorityStore
{
    Task<byte[]?> GetActivePkcs12Async();

    Task StorePkcs12Async(byte[] pkcs12, string version);

    Task<CertificateAuthorityRecord?> GetActiveRecordAsync();

    Task StoreCertificateAsync(byte[] certificateData, string version, string? keyVaultKeyIdentifier);

    Task<IReadOnlyList<X509Certificate2>> GetAllCertificatesAsync();
}
