using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

/// <summary>
/// Shared helpers for creating test CAs and leaf certificates.
/// </summary>
internal static class TestCertificateHelper
{
    public const string TrustDomain = "test.platform";

    public static MachineAuthenticationOptions DefaultOptions => new()
    {
        TrustDomain = TrustDomain,
        CertificateLifetimeDays = 365,
        MinCertificateLifetimeDays = 1,
        MaxCertificateLifetimeDays = 3650,
        CaLifetimeYears = 20,
        RenewalWindowDays = 30,
        RenewalOverlapDays = 7,
    };

    public static (X509Certificate2 CaCert, X509SignatureGenerator Generator, RSA CaKey) CreateCa(
        int lifetimeYears = 20)
    {
        var caKey = RSA.Create(2048); // smaller for test speed
        var subjectDn = new X500DistinguishedName($"CN=Test CA, O={TrustDomain}");
        var request = new CertificateRequest(subjectDn, caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddYears(lifetimeYears);
        var caCert = request.CreateSelfSigned(notBefore, notAfter);

        var generator = X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1);
        return (caCert, generator, caKey);
    }

    public static X509Certificate2 IssueMachineLeaf(
        X509Certificate2 caCert,
        X509SignatureGenerator generator,
        string organization,
        string project,
        string machineAccessId,
        int lifetimeDays = 365)
    {
        var spiffeUri = $"spiffe://{TrustDomain}/org/{organization}/project/{project}/machine/{machineAccessId}";
        return IssueLeaf(caCert, generator, machineAccessId, organization, project, spiffeUri, lifetimeDays);
    }

    public static X509Certificate2 IssueSharedLeaf(
        X509Certificate2 caCert,
        X509SignatureGenerator generator,
        string organization,
        string project,
        string label,
        int lifetimeDays = 365)
    {
        var spiffeUri = $"spiffe://{TrustDomain}/org/{organization}/project/{project}/shared/{label}";
        return IssueLeaf(caCert, generator, label, organization, project, spiffeUri, lifetimeDays);
    }

    public static X509Certificate2 IssueLeafWithCustomSan(
        X509Certificate2 caCert,
        X509SignatureGenerator generator,
        string? sanUri = null,
        int lifetimeDays = 365,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null,
        bool includeClientAuthEku = true)
    {
        using var leafKey = RSA.Create(2048);
        var subjectDn = new X500DistinguishedName("CN=Custom Leaf");
        var request = new CertificateRequest(subjectDn, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));

        if (includeClientAuthEku)
        {
            request.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    new OidCollection { new("1.3.6.1.5.5.7.3.2") }, false));
        }

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(caCert, true, false));

        if (sanUri is not null)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddUri(new Uri(sanUri));
            request.CertificateExtensions.Add(sanBuilder.Build());
        }

        var serial = RandomNumberGenerator.GetBytes(16);
        var nb = notBefore ?? DateTimeOffset.UtcNow;
        var na = notAfter ?? nb.AddDays(lifetimeDays);

        var leafCert = request.Create(caCert.SubjectName, generator, nb, na, serial);
        return leafCert.CopyWithPrivateKey(leafKey);
    }

    private static X509Certificate2 IssueLeaf(
        X509Certificate2 caCert,
        X509SignatureGenerator generator,
        string cn,
        string organization,
        string project,
        string spiffeUri,
        int lifetimeDays)
    {
        return IssueLeafWithCustomSan(caCert, generator, spiffeUri, lifetimeDays);
    }

    public static CertificateAuthorityMaterial CreateCaMaterial()
    {
        var (caCert, generator, _) = CreateCa();
        return new CertificateAuthorityMaterial(caCert, generator);
    }
}
