using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller;

public class ConfigurationCertificateAuthorityStore : ICertificateAuthorityStore
{
    private byte[]? _pkcs12;

    public Task<byte[]?> GetActivePkcs12Async()
    {
        return Task.FromResult(_pkcs12);
    }

    public Task StorePkcs12Async(byte[] pkcs12, string version)
    {
        _pkcs12 = pkcs12;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<X509Certificate2>> GetAllCertificatesAsync()
    {
        if (_pkcs12 is null)
        {
            return Task.FromResult<IReadOnlyList<X509Certificate2>>([]);
        }

        var cert = X509CertificateLoader.LoadPkcs12(_pkcs12, null);
        return Task.FromResult<IReadOnlyList<X509Certificate2>>([cert]);
    }
}
