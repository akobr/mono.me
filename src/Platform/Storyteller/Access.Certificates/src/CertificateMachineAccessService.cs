using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class CertificateMachineAccessService
{
    private readonly IClientCertificateAuthority _authority;
    private readonly IClientCertificateStore _store;
    private readonly IOptions<MachineAuthenticationOptions> _options;
    private readonly ILogger<CertificateMachineAccessService> _logger;

    public CertificateMachineAccessService(
        IClientCertificateAuthority authority,
        IClientCertificateStore store,
        IOptions<MachineAuthenticationOptions> options,
        ILogger<CertificateMachineAccessService> logger)
    {
        _authority = authority;
        _store = store;
        _options = options;
        _logger = logger;
    }

    public async Task<MachineAccess> CreateCertificateAsync(MachineAccessCreate model)
    {
        var opts = _options.Value;
        var lifetimeDays = ResolveLifetimeDays(model.CertificateLifetimeDays, opts);

        var id = Guid.NewGuid().ToString("D");
        var issued = await _authority.IssueCertificateAsync(
            model.Organization, model.Project, id, model.Scope, lifetimeDays);

        var record = new CertificateRecord(
            MachineAccessId: id,
            Thumbprint: issued.Thumbprint,
            PreviousThumbprint: null,
            PreviousValidUntil: null,
            SerialNumber: issued.SerialNumber,
            NotBefore: issued.NotBefore,
            NotAfter: issued.NotAfter,
            IsRevoked: false,
            Scope: model.Scope,
            AnnotationKey: model.AnnotationKey,
            LastRenewalAt: null);

        await _store.StoreAsync(model.Organization, model.Project, record);

        return new MachineAccess
        {
            Id = id,
            ObjectId = id,
            AccessKey = string.Empty,
            Scope = model.Scope,
            AnnotationKey = model.AnnotationKey,
            CredentialKind = MachineCredentialKind.Certificate,
            Certificate = Convert.ToBase64String(issued.Pkcs12),
            CertificatePassword = issued.Password,
            CertificateThumbprint = issued.Thumbprint,
        };
    }

    public async Task<MachineAccess> ExtendWithCertificateAsync(MachineAccess existingAccess, MachineAccessCreate model)
    {
        var opts = _options.Value;
        var lifetimeDays = ResolveLifetimeDays(model.CertificateLifetimeDays, opts);

        var issued = await _authority.IssueCertificateAsync(
            model.Organization, model.Project, existingAccess.Id, model.Scope, lifetimeDays);

        var record = new CertificateRecord(
            MachineAccessId: existingAccess.Id,
            Thumbprint: issued.Thumbprint,
            PreviousThumbprint: null,
            PreviousValidUntil: null,
            SerialNumber: issued.SerialNumber,
            NotBefore: issued.NotBefore,
            NotAfter: issued.NotAfter,
            IsRevoked: false,
            Scope: model.Scope,
            AnnotationKey: model.AnnotationKey,
            LastRenewalAt: null);

        await _store.StoreAsync(model.Organization, model.Project, record);

        return existingAccess with
        {
            CredentialKind = MachineCredentialKind.CertificateAndApiKey,
            Certificate = Convert.ToBase64String(issued.Pkcs12),
            CertificatePassword = issued.Password,
            CertificateThumbprint = issued.Thumbprint,
        };
    }

    public async Task RevokeCertificateAsync(string organization, string project, string machineAccessId)
    {
        await _store.RevokeAsync(organization, project, machineAccessId);
        _logger.LogInformation("Certificate revoked for machine {MachineAccessId}", machineAccessId);
    }

    private static int ResolveLifetimeDays(int? requestedLifetime, MachineAuthenticationOptions opts)
    {
        var days = requestedLifetime ?? opts.CertificateLifetimeDays;
        return Math.Clamp(days, opts.MinCertificateLifetimeDays, opts.MaxCertificateLifetimeDays);
    }
}
