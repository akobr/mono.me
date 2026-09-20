using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class ClientCertificateValidator : IClientCertificateValidator
{
    private readonly ICertificateAuthorityProvider _caProvider;
    private readonly IClientCertificateStore _certificateStore;
    private readonly ISharedCertificateStore _sharedCertificateStore;
    private readonly IOptions<MachineAuthenticationOptions> _options;
    private readonly ILogger<ClientCertificateValidator> _logger;

    public ClientCertificateValidator(
        ICertificateAuthorityProvider caProvider,
        IClientCertificateStore certificateStore,
        ISharedCertificateStore sharedCertificateStore,
        IOptions<MachineAuthenticationOptions> options,
        ILogger<ClientCertificateValidator> logger)
    {
        _caProvider = caProvider;
        _certificateStore = certificateStore;
        _sharedCertificateStore = sharedCertificateStore;
        _options = options;
        _logger = logger;
    }

    public async Task<ClientCertificateValidationResult?> ValidateAsync(X509Certificate2 clientCertificate)
    {
        var opts = _options.Value;

        // 1. Parse identity from SAN.
        var identity = CertificateIdentity.TryParse(clientCertificate, opts.TrustDomain);

        if (identity is null)
        {
            _logger.LogWarning("Certificate has no valid platform SAN (trust domain: {TrustDomain})", opts.TrustDomain);
            return null;
        }

        // 2. Chain validation against our CA.
        var trusted = await _caProvider.GetTrustedAsync();

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.2")); // clientAuth

        foreach (var ca in trusted)
        {
            chain.ChainPolicy.CustomTrustStore.Add(ca);
        }

        if (!chain.Build(clientCertificate))
        {
            _logger.LogWarning("Certificate chain validation failed for {Thumbprint}: {Status}",
                clientCertificate.Thumbprint,
                string.Join(", ", chain.ChainStatus.Select(s => s.StatusInformation)));
            return null;
        }

        // 3. Validity window check.
        var now = DateTimeOffset.UtcNow;

        if (now < clientCertificate.NotBefore || now > clientCertificate.NotAfter)
        {
            _logger.LogWarning("Certificate {Thumbprint} is outside validity window", clientCertificate.Thumbprint);
            return null;
        }

        // 4. Branch on kind: machine vs shared.
        if (identity.Kind == ClientCertificateKind.Machine)
        {
            return await ValidateMachineCertificateAsync(clientCertificate, identity);
        }

        // Shared certificates: SAN + chain validation, then check revocation status.
        var thumbprint = clientCertificate.Thumbprint;
        var sharedCerts = await _sharedCertificateStore.ListAsync(identity.Organization, identity.Project);
        var sharedRecord = sharedCerts.FirstOrDefault(
            c => string.Equals(c.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));

        if (sharedRecord is { IsRevoked: true })
        {
            _logger.LogWarning("Shared certificate {Thumbprint} is revoked", thumbprint);
            return null;
        }

        // Identity/scope comes from the API key, not the certificate.
        return new ClientCertificateValidationResult(
            identity.Organization,
            identity.Project,
            null,
            default,
            thumbprint,
            ClientCertificateKind.Shared,
            null);
    }

    private async Task<ClientCertificateValidationResult?> ValidateMachineCertificateAsync(
        X509Certificate2 clientCertificate,
        CertificateIdentity identity)
    {
        var record = await _certificateStore.GetByMachineAccessIdAsync(
            identity.Organization, identity.Project, identity.MachineAccessId!);

        if (record is null)
        {
            _logger.LogWarning("No certificate record found for machine {MachineAccessId}", identity.MachineAccessId);
            return null;
        }

        if (record.IsRevoked)
        {
            _logger.LogWarning("Certificate for machine {MachineAccessId} is revoked", identity.MachineAccessId);
            return null;
        }

        // Thumbprint pinning: current or previous (during overlap).
        var thumbprint = clientCertificate.Thumbprint;
        var isCurrentThumbprint = string.Equals(thumbprint, record.Thumbprint, StringComparison.OrdinalIgnoreCase);
        var isPreviousThumbprint = record.PreviousThumbprint is not null
            && string.Equals(thumbprint, record.PreviousThumbprint, StringComparison.OrdinalIgnoreCase)
            && record.PreviousValidUntil.HasValue
            && DateTimeOffset.UtcNow <= record.PreviousValidUntil.Value;

        if (!isCurrentThumbprint && !isPreviousThumbprint)
        {
            _logger.LogWarning("Certificate thumbprint mismatch for machine {MachineAccessId}", identity.MachineAccessId);
            return null;
        }

        return new ClientCertificateValidationResult(
            identity.Organization,
            identity.Project,
            identity.MachineAccessId,
            record.Scope,
            thumbprint,
            ClientCertificateKind.Machine,
            record.AnnotationKey);
    }
}
