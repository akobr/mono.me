using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Utils.Async;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class LocalCertificateAuthorityProvider : ICertificateAuthorityProvider
{
    private readonly ICertificateAuthorityStore _store;
    private readonly IOptions<MachineAuthenticationOptions> _options;
    private readonly ILogger<LocalCertificateAuthorityProvider> _logger;
    private readonly CachedAsync<CertificateAuthorityMaterial> _cachedMaterial;

    public LocalCertificateAuthorityProvider(
        ICertificateAuthorityStore store,
        IOptions<MachineAuthenticationOptions> options,
        ILogger<LocalCertificateAuthorityProvider> logger)
    {
        _store = store;
        _options = options;
        _logger = logger;
        _cachedMaterial = new CachedAsync<CertificateAuthorityMaterial>(
            LoadOrBootstrapAsync,
            options.Value.Authority.RefreshInterval,
            ex => logger.LogError(ex, "Failed to refresh CA material; serving stale value"));
    }

    public Task<CertificateAuthorityMaterial> GetActiveAsync()
    {
        return _cachedMaterial.GetValueAsync();
    }

    public async Task<IReadOnlyList<X509Certificate2>> GetTrustedAsync()
    {
        return await _store.GetAllCertificatesAsync();
    }

    private async Task<CertificateAuthorityMaterial> LoadOrBootstrapAsync()
    {
        var opts = _options.Value;

        var pkcs12 = await _store.GetActivePkcs12Async();

        if (pkcs12 is not null)
        {
            var cert = X509CertificateLoader.LoadPkcs12(pkcs12, null,
                X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
            var rsa = cert.GetRSAPrivateKey()!;
            var generator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);
            return new CertificateAuthorityMaterial(cert, generator);
        }

        if (!opts.Authority.IsAutoBootstrapEnabled)
        {
            throw new InvalidOperationException(
                "No CA certificate found and auto-bootstrap is disabled. " +
                "Please provision a CA certificate or enable auto-bootstrap.");
        }

        _logger.LogInformation("No CA certificate found — bootstrapping a new local CA");
        return await BootstrapAsync(opts);
    }

    private async Task<CertificateAuthorityMaterial> BootstrapAsync(MachineAuthenticationOptions opts)
    {
        using var caKey = RSA.Create(4096);

        var subjectDn = new X500DistinguishedName($"CN=2S Platform Machine CA, O={opts.TrustDomain}");
        var request = new CertificateRequest(subjectDn, caKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Basic Constraints: CA = true
        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: true,
                pathLengthConstraint: 0,
                critical: true));

        // Key Usage: KeyCertSign, CrlSign
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));

        // Subject Key Identifier
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddYears(opts.CaLifetimeYears);

        var caCert = request.CreateSelfSigned(notBefore, notAfter);

        var pkcs12 = caCert.Export(X509ContentType.Pkcs12);
        var version = "1";
        await _store.StorePkcs12Async(pkcs12, version);

        // Reload to get a proper persistent key.
        var reloaded = X509CertificateLoader.LoadPkcs12(pkcs12, null,
            X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
        var rsa = reloaded.GetRSAPrivateKey()!;
        var generator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);

        _logger.LogInformation("Local CA bootstrapped: {Subject}, valid until {NotAfter}", reloaded.Subject, reloaded.NotAfter);

        return new CertificateAuthorityMaterial(reloaded, generator);
    }
}
