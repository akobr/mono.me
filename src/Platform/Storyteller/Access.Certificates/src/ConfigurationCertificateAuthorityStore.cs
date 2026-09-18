using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller;

public class ConfigurationCertificateAuthorityStore : ICertificateAuthorityStore
{
    private byte[]? _pkcs12;
    private byte[]? _certificateData;
    private string? _keyVaultKeyIdentifier;
    private string _version = "1";

    public Task<byte[]?> GetActivePkcs12Async()
    {
        return Task.FromResult(_pkcs12);
    }

    public Task StorePkcs12Async(byte[] pkcs12, string version)
    {
        _pkcs12 = pkcs12;
        _version = version;
        return Task.CompletedTask;
    }

    public Task<CertificateAuthorityRecord?> GetActiveRecordAsync()
    {
        if (_pkcs12 is not null)
        {
            using var cert = X509CertificateLoader.LoadPkcs12(_pkcs12, null);
            return Task.FromResult<CertificateAuthorityRecord?>(
                new CertificateAuthorityRecord(cert.RawData, _pkcs12, null, _version));
        }

        if (_certificateData is not null)
        {
            return Task.FromResult<CertificateAuthorityRecord?>(
                new CertificateAuthorityRecord(_certificateData, null, _keyVaultKeyIdentifier, _version));
        }

        return Task.FromResult<CertificateAuthorityRecord?>(null);
    }

    public Task StoreCertificateAsync(byte[] certificateData, string version, string? keyVaultKeyIdentifier)
    {
        _certificateData = certificateData;
        _keyVaultKeyIdentifier = keyVaultKeyIdentifier;
        _version = version;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<X509Certificate2>> GetAllCertificatesAsync()
    {
        if (_pkcs12 is not null)
        {
            var cert = X509CertificateLoader.LoadPkcs12(_pkcs12, null);
            return Task.FromResult<IReadOnlyList<X509Certificate2>>([cert]);
        }

        if (_certificateData is not null)
        {
            var cert = X509CertificateLoader.LoadCertificate(_certificateData);
            return Task.FromResult<IReadOnlyList<X509Certificate2>>([cert]);
        }

        return Task.FromResult<IReadOnlyList<X509Certificate2>>([]);
    }
}
