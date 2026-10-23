using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace _42.Platform.Storyteller.Sdk.UnitTests;

// Self-signed client certificates as PKCS#12, like sform account machine create writes them.
internal static class TestCertificates
{
    public const string Password = "pfx-password";

    public static byte[] Pkcs12(string? password = Password)
    {
        using var certificate = Create();
        return certificate.Export(X509ContentType.Pkcs12, password);
    }

    public static byte[] Pkcs12WithoutPrivateKey()
    {
        using var certificate = Create();
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return publicOnly.Export(X509ContentType.Pkcs12, Password);
    }

    public static X509Certificate2 WithPrivateKey()
    {
        return X509CertificateLoader.LoadPkcs12(Pkcs12(), Password);
    }

    public static X509Certificate2 WithoutPrivateKey()
    {
        using var certificate = Create();
        return X509CertificateLoader.LoadCertificate(certificate.RawData);
    }

    private static X509Certificate2 Create()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=storyteller-sdk-test", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }
}
