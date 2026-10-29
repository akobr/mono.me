namespace _42.Platform.Storyteller.Accessing;

public class MachineAuthenticationOptions
{
    public const string SectionName = "MachineAuth";

    // The header App Service fills with the client certificate of the TLS handshake.
    public const string DefaultCertificateHeaderName = "X-ARR-ClientCert";

    public MachineCredentialKind DefaultCredentialKind { get; set; } = MachineCredentialKind.ApiKey;

    public string CertificateHeaderName { get; set; } = DefaultCertificateHeaderName;

    public bool IsCertificateHeaderFromClientAllowed { get; set; }

    public string TrustDomain { get; set; } = "2s.platform";

    public int CertificateLifetimeDays { get; set; } = 365;

    public int MinCertificateLifetimeDays { get; set; } = 1;

    public int MaxCertificateLifetimeDays { get; set; } = 3650;

    public int CaLifetimeYears { get; set; } = 20;

    public int RenewalWindowDays { get; set; } = 30;

    public int RenewalOverlapDays { get; set; } = 7;

    public CertificateAuthorityOptions Authority { get; set; } = new();
}
