using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface ICertificateAuthorityProvider
{
    Task<CertificateAuthorityMaterial> GetActiveAsync();

    Task<IReadOnlyList<X509Certificate2>> GetTrustedAsync();
}
