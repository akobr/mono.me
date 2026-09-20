using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface ICertificateRenewalService
{
    Task<CertificateRenewalResult> RenewAsync(
        string organization,
        string project,
        string machineAccessId,
        string presentingThumbprint);
}
