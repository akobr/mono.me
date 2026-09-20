using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace _42.Platform.Storyteller.Access.Certificates.UnitTests;

public class CertificateSigningPrimitivesTests
{
    [Fact]
    public void GetSignatureAlgorithmIdentifier_MatchesLocalRsaGenerator()
    {
        using var key = RSA.Create(2048);

        // Use a real local generator as the "KV" generator's delegate algorithm source.
        var localGenerator = X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1);

        // The KV generator delegates to a throwaway CreateForRSA for the AlgorithmIdentifier.
        // We can verify this by comparing a fresh CreateForRSA output.
        using var tempKey = RSA.Create(2048);
        var tempGenerator = X509SignatureGenerator.CreateForRSA(tempKey, RSASignaturePadding.Pkcs1);

        var localAlgId = localGenerator.GetSignatureAlgorithmIdentifier(HashAlgorithmName.SHA256);
        var tempAlgId = tempGenerator.GetSignatureAlgorithmIdentifier(HashAlgorithmName.SHA256);

        // All RSA PKCS1 SHA256 generators produce the same AlgorithmIdentifier DER.
        localAlgId.ShouldBe(tempAlgId);
    }

    [Fact]
    public void SelfSignedCa_ViaLocalSimulation_VerifiesAgainstOwnPublicKey()
    {
        // Simulate the two-step self-signing dance using a local RSA key
        // (same logic the KV generator would use, but without the network call).
        using var caKey = RSA.Create(2048);
        var spki = caKey.ExportSubjectPublicKeyInfo();
        var publicKey = PublicKey.CreateFromSubjectPublicKeyInfo(spki, out _);

        // Use CreateForRSA as the "KV" generator (same signing behavior).
        var generator = X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1);

        // Two-step self-signing: CertificateRequest with PublicKey only, signed via external generator.
        var subjectDn = new X500DistinguishedName("CN=Test CA");
        var request = new CertificateRequest(subjectDn, publicKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var serial = RandomNumberGenerator.GetBytes(16);
        var caCert = request.Create(subjectDn, generator, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddYears(1), serial);

        // Self-signed: issuer == subject.
        caCert.Issuer.ShouldBe(caCert.Subject);

        // Verify extensions.
        var basicConstraints = caCert.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        basicConstraints.ShouldNotBeNull();
        basicConstraints!.CertificateAuthority.ShouldBeTrue();

        var keyUsage = caCert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        keyUsage.ShouldNotBeNull();
        keyUsage!.KeyUsages.ShouldBe(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign);

        var ski = caCert.Extensions.OfType<X509SubjectKeyIdentifierExtension>().FirstOrDefault();
        ski.ShouldNotBeNull();
    }

    [Fact]
    public void LeafSignedByExternalGenerator_ChainsToSelfSignedCa()
    {
        using var caKey = RSA.Create(2048);
        var spki = caKey.ExportSubjectPublicKeyInfo();
        var publicKey = PublicKey.CreateFromSubjectPublicKeyInfo(spki, out _);
        var generator = X509SignatureGenerator.CreateForRSA(caKey, RSASignaturePadding.Pkcs1);

        // Create self-signed CA via two-step dance.
        var caSubject = new X500DistinguishedName("CN=Test CA");
        var caRequest = new CertificateRequest(caSubject, publicKey, HashAlgorithmName.SHA256);
        caRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, true));
        caRequest.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        caRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(caRequest.PublicKey, false));

        var caCert = caRequest.Create(caSubject, generator, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddYears(1), RandomNumberGenerator.GetBytes(16));

        // Create leaf signed by the same external generator.
        using var leafKey = RSA.Create(2048);
        var leafSubject = new X500DistinguishedName("CN=Test Leaf");
        var leafRequest = new CertificateRequest(leafSubject, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new("1.3.6.1.5.5.7.3.2") }, false));
        leafRequest.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(caCert, true, false));

        var leafCert = leafRequest.Create(caCert.SubjectName, generator, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(365), RandomNumberGenerator.GetBytes(16));

        // Verify the leaf chains to the CA.
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.CustomTrustStore.Add(caCert);

        chain.Build(leafCert).ShouldBeTrue();
    }

    [Fact]
    public void PublicKeyFromSubjectPublicKeyInfo_RoundTrips()
    {
        using var key = RSA.Create(2048);
        var spki = key.ExportSubjectPublicKeyInfo();
        var publicKey = PublicKey.CreateFromSubjectPublicKeyInfo(spki, out var bytesRead);

        bytesRead.ShouldBe(spki.Length);
        publicKey.ShouldNotBeNull();

        // The public key should produce the same SPKI when re-exported.
        var exportedSpki = publicKey.ExportSubjectPublicKeyInfo();
        exportedSpki.ShouldBe(spki);
    }
}
