using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class SharedCertificateService
{
    private readonly IClientCertificateAuthority _authority;
    private readonly ISharedCertificateStore _store;
    private readonly IMachineAuthenticationPolicyStore _policyStore;
    private readonly IOptions<MachineAuthenticationOptions> _options;
    private readonly ILogger<SharedCertificateService> _logger;

    public SharedCertificateService(
        IClientCertificateAuthority authority,
        ISharedCertificateStore store,
        IMachineAuthenticationPolicyStore policyStore,
        IOptions<MachineAuthenticationOptions> options,
        ILogger<SharedCertificateService> logger)
    {
        _authority = authority;
        _store = store;
        _policyStore = policyStore;
        _options = options;
        _logger = logger;
    }

    public async Task<SharedCertificate> IssueAsync(
        string organization,
        string project,
        string label,
        int? requestedLifetimeDays)
    {
        var opts = _options.Value;
        var policy = await _policyStore.GetEffectiveAsync(organization, project, opts);

        // Shared certificates follow CertificateAndApiKey; listing and revoking stay open, to clean up.
        if (!policy.AllowsSharedCertificates())
        {
            throw new MachineAccessNotSupportedException(
                $"The project {organization}.{project} does not enable {MachineCredentialKind.CertificateAndApiKey}, which shared certificates belong to.");
        }

        var lifetimeDays = Math.Clamp(
            requestedLifetimeDays ?? opts.CertificateLifetimeDays,
            opts.MinCertificateLifetimeDays,
            opts.MaxCertificateLifetimeDays);

        // Enforce label uniqueness by appending timestamp on duplicate.
        if (await _store.LabelExistsAsync(organization, project, label))
        {
            label = $"{label}-{DateTimeOffset.UtcNow:yyyy-MM-dd-HH-mm-ss}";
            _logger.LogInformation("Duplicate shared certificate label; disambiguated to {Label}", label);
        }

        var issued = await _authority.IssueSharedCertificateAsync(organization, project, label, lifetimeDays);

        var certificate = new SharedCertificate
        {
            Thumbprint = issued.Thumbprint,
            Label = label,
            NotBefore = issued.NotBefore,
            NotAfter = issued.NotAfter,
            Certificate = Convert.ToBase64String(issued.Pkcs12),
            CertificatePassword = issued.Password,
        };

        await _store.StoreAsync(organization, project, certificate);

        _logger.LogInformation(
            "Shared certificate issued for {Organization}/{Project} label={Label} thumbprint={Thumbprint}",
            organization,
            project,
            label,
            issued.Thumbprint);

        return certificate;
    }

    public async Task<IReadOnlyList<SharedCertificate>> ListAsync(string organization, string project)
    {
        return await _store.ListAsync(organization, project);
    }

    public async Task<bool> RevokeAsync(string organization, string project, string thumbprint)
    {
        var result = await _store.RevokeAsync(organization, project, thumbprint);

        if (result)
        {
            _logger.LogInformation(
                "Shared certificate revoked: {Organization}/{Project} thumbprint={Thumbprint}",
                organization,
                project,
                thumbprint);
        }

        return result;
    }
}
