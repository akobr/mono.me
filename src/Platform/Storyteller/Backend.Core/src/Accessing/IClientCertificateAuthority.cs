using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface IClientCertificateAuthority
{
    Task<IssuedCertificate> IssueCertificateAsync(
        string organization,
        string project,
        string machineAccessId,
        MachineAccessScope scope,
        int lifetimeDays);

    Task<IssuedCertificate> IssueSharedCertificateAsync(
        string organization,
        string project,
        string label,
        int lifetimeDays);
}
