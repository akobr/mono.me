namespace _42.Platform.Storyteller.Accessing;

public interface IClientCertificateStore
{
    Task<CertificateRecord?> GetByMachineAccessIdAsync(string organization, string project, string machineAccessId);

    Task<CertificateRecord?> GetByThumbprintAsync(string organization, string project, string thumbprint);

    Task StoreAsync(string organization, string project, CertificateRecord record);

    Task RevokeAsync(string organization, string project, string machineAccessId);
}
