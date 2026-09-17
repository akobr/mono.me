using System.Security.Cryptography.X509Certificates;

namespace _42.Platform.Storyteller.Accessing;

public interface ICertificateAuthorityStore
{
    Task<byte[]?> GetActivePkcs12Async();

    Task StorePkcs12Async(byte[] pkcs12, string version);

    Task<IReadOnlyList<X509Certificate2>> GetAllCertificatesAsync();
}
