namespace _42.Platform.Storyteller.Accessing;

public class CertificateAuthorityOptions
{
    public CertificateAuthorityKind Kind { get; set; } = CertificateAuthorityKind.Cosmos;

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(1);

    public bool IsAutoBootstrapEnabled { get; set; } = true;
}
