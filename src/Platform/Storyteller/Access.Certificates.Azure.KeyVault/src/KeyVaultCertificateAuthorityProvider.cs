using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Utils.Async;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class KeyVaultCertificateAuthorityProvider : ICertificateAuthorityProvider
{
    private readonly KeyClient _keyClient;
    private readonly ICertificateAuthorityStore _store;
    private readonly IOptions<MachineAuthenticationOptions> _options;
    private readonly KeyVaultCertificateAuthorityOptions _kvOptions;
    private readonly ILogger<KeyVaultCertificateAuthorityProvider> _logger;
    private readonly CachedAsync<CertificateAuthorityMaterial> _cachedMaterial;
    private readonly CachedAsync<IReadOnlyList<X509Certificate2>> _cachedTrusted;

    public KeyVaultCertificateAuthorityProvider(
        IAzureClientFactory<KeyClient> keyClientFactory,
        ICertificateAuthorityStore store,
        IOptions<MachineAuthenticationOptions> options,
        IOptions<KeyVaultCertificateAuthorityOptions> kvOptions,
        ILogger<KeyVaultCertificateAuthorityProvider> logger)
    {
        _kvOptions = kvOptions.Value;
        _keyClient = keyClientFactory.CreateClient(_kvOptions.VaultName);
        _store = store;
        _options = options;
        _logger = logger;
        _cachedMaterial = new CachedAsync<CertificateAuthorityMaterial>(
            LoadOrBootstrapAsync,
            options.Value.Authority.RefreshInterval,
            ex => logger.LogError(ex, "Failed to refresh CA material from Key Vault; serving stale value"));
        _cachedTrusted = new CachedAsync<IReadOnlyList<X509Certificate2>>(
            store.GetAllCertificatesAsync,
            options.Value.Authority.RefreshInterval,
            ex => logger.LogError(ex, "Failed to refresh trusted CA list from Key Vault; serving stale value"));

        var authorityKind = options.Value.Authority.Kind;

        if (authorityKind != CertificateAuthorityKind.KeyVault)
        {
            logger.LogWarning(
                "KeyVaultCertificateAuthorityProvider is registered but MachineAuth:Authority:Kind is {Kind}, not KeyVault. " +
                "The DI registration overrides the config value",
                authorityKind);
        }
    }

    public Task<CertificateAuthorityMaterial> GetActiveAsync()
    {
        return _cachedMaterial.GetValueAsync();
    }

    public Task<IReadOnlyList<X509Certificate2>> GetTrustedAsync()
    {
        return _cachedTrusted.GetValueAsync();
    }

    private async Task<CertificateAuthorityMaterial> LoadOrBootstrapAsync()
    {
        var opts = _options.Value;

        var record = await _store.GetActiveRecordAsync();

        if (record?.KeyVaultKeyIdentifier is not null)
        {
            return LoadFromRecord(record);
        }

        if (record?.Pkcs12Data is not null)
        {
            _logger.LogWarning(
                "Active CA record has PKCS#12 data but no Key Vault key identifier. " +
                "This CA was bootstrapped with a local key. A new Key Vault CA will be created");
        }

        if (!opts.Authority.IsAutoBootstrapEnabled)
        {
            throw new InvalidOperationException(
                "No Key Vault CA certificate found and auto-bootstrap is disabled. " +
                "Please provision a CA key in Key Vault or enable auto-bootstrap.");
        }

        _logger.LogInformation("No CA certificate found — bootstrapping a new CA in Key Vault");
        return await BootstrapAsync(opts);
    }

    private CertificateAuthorityMaterial LoadFromRecord(CertificateAuthorityRecord record)
    {
        var cert = X509CertificateLoader.LoadCertificate(record.CertificateData);
        var identifier = new KeyVaultKeyIdentifier(new Uri(record.KeyVaultKeyIdentifier!));
        var cryptoClient = _keyClient.GetCryptographyClient(identifier.Name, identifier.Version);
        var generator = new KeyVaultSignatureGenerator(cryptoClient, cert.PublicKey);

        _logger.LogDebug(
            "Loaded CA from store: {Subject}, Key Vault key {KeyId}",
            cert.Subject,
            record.KeyVaultKeyIdentifier);

        return new CertificateAuthorityMaterial(cert, generator);
    }

    private async Task<CertificateAuthorityMaterial> BootstrapAsync(MachineAuthenticationOptions opts)
    {
        var keyName = _kvOptions.KeyName;

        // Create RSA-4096 key in Key Vault (non-exportable).
        // CreateRsaKeyAsync creates a new version if the key already exists.
        var createOptions = new CreateRsaKeyOptions(keyName)
        {
            KeySize = 4096,
            Exportable = false,
        };
        createOptions.KeyOperations.Add(KeyOperation.Sign);
        createOptions.KeyOperations.Add(KeyOperation.Verify);

        var keyResponse = await _keyClient.CreateRsaKeyAsync(createOptions);
        var key = keyResponse.Value;

        // Build PublicKey from the Key Vault key.
        using var rsa = key.Key.ToRSA();
        var spki = rsa.ExportSubjectPublicKeyInfo();
        var publicKey = PublicKey.CreateFromSubjectPublicKeyInfo(spki, out _);

        // Create signature generator for this key version.
        var cryptoClient = _keyClient.GetCryptographyClient(keyName, key.Properties.Version);
        var generator = new KeyVaultSignatureGenerator(cryptoClient, publicKey);

        // Build CA certificate request (two-step self-signing dance).
        var subjectDn = new X500DistinguishedName($"CN=2S Platform Machine CA, O={opts.TrustDomain}");
        var request = new CertificateRequest(subjectDn, publicKey, HashAlgorithmName.SHA256);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: true,
                hasPathLengthConstraint: true,
                pathLengthConstraint: 0,
                critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign,
                critical: true));

        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddYears(opts.CaLifetimeYears);
        var serial = RandomNumberGenerator.GetBytes(16);

        // Self-sign: Create() with same subject as issuer and the KV signature generator.
        var caCert = request.Create(subjectDn, generator, notBefore, notAfter, serial);

        // Try to store the certificate in Cosmos (conditional create — may lose to a race).
        var version = key.Properties.Version;
        var keyIdentifier = key.Id.ToString();
        await _store.StoreCertificateAsync(caCert.RawData, version, keyIdentifier);

        // Always re-read to get the definitive record (ours or the winner's).
        // This ensures we use the key version that matches the stored CA certificate.
        var definitive = await _store.GetActiveRecordAsync();

        if (definitive?.KeyVaultKeyIdentifier is not null)
        {
            if (!string.Equals(definitive.KeyVaultKeyIdentifier, keyIdentifier, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Another instance won the CA bootstrap race. Using their key {KeyId}",
                    definitive.KeyVaultKeyIdentifier);
            }
            else
            {
                _logger.LogInformation(
                    "Key Vault CA bootstrapped: {Subject}, key {KeyId}, valid until {NotAfter}",
                    caCert.Subject,
                    keyIdentifier,
                    notAfter);
            }

            return LoadFromRecord(definitive);
        }

        throw new InvalidOperationException(
            "CA bootstrap failed: no record found after store attempt.");
    }
}
