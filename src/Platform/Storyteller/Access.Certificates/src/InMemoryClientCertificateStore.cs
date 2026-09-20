using System.Collections.Concurrent;
using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller;

public class InMemoryClientCertificateStore : IClientCertificateStore
{
    private readonly ConcurrentDictionary<string, CertificateRecord> _records = new();

    public Task<CertificateRecord?> GetByMachineAccessIdAsync(string organization, string project, string machineAccessId)
    {
        var key = MakeKey(organization, project, machineAccessId);
        _records.TryGetValue(key, out var record);
        return Task.FromResult(record);
    }

    public Task<CertificateRecord?> GetByThumbprintAsync(string organization, string project, string thumbprint)
    {
        var record = _records.Values.FirstOrDefault(r =>
            string.Equals(r.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(record);
    }

    public Task StoreAsync(string organization, string project, CertificateRecord record)
    {
        var key = MakeKey(organization, project, record.MachineAccessId);
        _records[key] = record;
        return Task.CompletedTask;
    }

    public Task RevokeAsync(string organization, string project, string machineAccessId)
    {
        var key = MakeKey(organization, project, machineAccessId);

        if (_records.TryGetValue(key, out var existing))
        {
            _records[key] = existing with { IsRevoked = true };
        }

        return Task.CompletedTask;
    }

    private static string MakeKey(string organization, string project, string machineAccessId)
        => $"{organization}:{project}:{machineAccessId}";
}
