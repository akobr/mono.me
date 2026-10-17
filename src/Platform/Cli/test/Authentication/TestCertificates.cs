using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace _42.Platform.Cli.UnitTests.Authentication;

// Self-signed client certificates with the SPIFFE URI SAN of Storyteller, as PKCS#12 like machine create returns.
internal static class TestCertificates
{
    public const string Password = "pfx-password";

    public static string MachineUri(string organization, string project, string machineId)
    {
        return $"spiffe://2s.platform/org/{organization}/project/{project}/machine/{machineId}";
    }

    public static string SharedUri(string organization, string project, string label)
    {
        return $"spiffe://2s.platform/org/{organization}/project/{project}/shared/{label}";
    }

    public static byte[] Pkcs12(string? spiffeUri, string? password = Password)
    {
        using var certificate = Create(spiffeUri);
        return certificate.Export(X509ContentType.Pkcs12, password);
    }

    public static byte[] Pkcs12WithoutPrivateKey(string spiffeUri)
    {
        using var certificate = Create(spiffeUri);
        using var publicOnly = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return publicOnly.Export(X509ContentType.Pkcs12, Password);
    }

    public static X509Certificate2 Load(byte[] pkcs12, string? password = Password)
    {
        return X509CertificateLoader.LoadPkcs12(pkcs12, password);
    }

    private static X509Certificate2 Create(string? spiffeUri)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=sform-test", key, HashAlgorithmName.SHA256);

        if (spiffeUri is not null)
        {
            var subjectAlternativeName = new SubjectAlternativeNameBuilder();
            subjectAlternativeName.AddUri(new Uri(spiffeUri));
            request.CertificateExtensions.Add(subjectAlternativeName.Build());
        }

        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
    }
}
