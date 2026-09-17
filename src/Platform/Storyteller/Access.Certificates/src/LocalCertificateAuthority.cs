using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class LocalCertificateAuthority : IClientCertificateAuthority
{
    private readonly ICertificateAuthorityProvider _caProvider;
    private readonly IOptions<MachineAuthenticationOptions> _options;
    private readonly ILogger<LocalCertificateAuthority> _logger;

    public LocalCertificateAuthority(
        ICertificateAuthorityProvider caProvider,
        IOptions<MachineAuthenticationOptions> options,
        ILogger<LocalCertificateAuthority> logger)
    {
        _caProvider = caProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<IssuedCertificate> IssueCertificateAsync(
        string organization,
        string project,
        string machineAccessId,
        MachineAccessScope scope,
        int lifetimeDays)
    {
        var opts = _options.Value;
        var identity = new CertificateIdentity(organization, project, machineAccessId, null, ClientCertificateKind.Machine);
        var spiffeUri = identity.ToSpiffeUri(opts.TrustDomain);
        var subjectDn = new X500DistinguishedName($"CN={machineAccessId}, O={organization}, OU={project}");

        return await IssueCertificateCoreAsync(subjectDn, spiffeUri, lifetimeDays);
    }

    public async Task<IssuedCertificate> IssueSharedCertificateAsync(
        string organization,
        string project,
        string label,
        int lifetimeDays)
    {
        var opts = _options.Value;
        var identity = new CertificateIdentity(organization, project, null, label, ClientCertificateKind.Shared);
        var spiffeUri = identity.ToSpiffeUri(opts.TrustDomain);
        var subjectDn = new X500DistinguishedName($"CN={label}, O={organization}, OU={project}");

        return await IssueCertificateCoreAsync(subjectDn, spiffeUri, lifetimeDays);
    }

    private async Task<IssuedCertificate> IssueCertificateCoreAsync(
        X500DistinguishedName subjectDn,
        string spiffeUri,
        int lifetimeDays)
    {
        var caMaterial = await _caProvider.GetActiveAsync();
        var caCert = caMaterial.Certificate;
        var generator = caMaterial.SignatureGenerator;

        // Clamp lifetime to the CA's remaining validity.
        var caRemainingDays = (int)(caCert.NotAfter.ToUniversalTime() - DateTime.UtcNow).TotalDays;
        if (lifetimeDays > caRemainingDays)
        {
            _logger.LogWarning(
                "Requested leaf lifetime {Requested} days clamped to CA remaining validity {Remaining} days",
                lifetimeDays, caRemainingDays);
            lifetimeDays = caRemainingDays;
        }

        using var leafKey = RSA.Create(2048);
        var request = new CertificateRequest(subjectDn, leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Key Usage: Digital Signature
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));

        // EKU: Client Authentication
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.2") },
                critical: false));

        // Subject Key Identifier
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        // Authority Key Identifier
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(caCert, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        // SAN with SPIFFE URI
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddUri(new Uri(spiffeUri));
        request.CertificateExtensions.Add(sanBuilder.Build());

        // Random 16-byte serial
        var serial = RandomNumberGenerator.GetBytes(16);

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddDays(lifetimeDays);

        var leafCert = request.Create(
            caCert.SubjectName,
            generator,
            notBefore,
            notAfter,
            serial);

        // Combine leaf with its private key.
        var leafWithKey = leafCert.CopyWithPrivateKey(leafKey);

        var password = GeneratePassword();
        var pkcs12 = leafWithKey.Export(X509ContentType.Pkcs12, password);
        var thumbprint = leafWithKey.Thumbprint;
        var serialNumber = Convert.ToHexString(serial);

        return new IssuedCertificate(pkcs12, password, thumbprint, serialNumber, notBefore, notAfter);
    }

    private static string GeneratePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
