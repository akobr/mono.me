using System.Collections.Concurrent;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Access.Certificates.IntegrationTests;

// An identity provider that issues client IDs like WorkOS (client_…) and records what it was asked.
public sealed class RecordingIdentityProviderMachineAccessService : IIdentityProviderMachineAccessService
{
    public ConcurrentQueue<string> Calls { get; } = new();

    public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        var suffix = Guid.NewGuid().ToString("N");
        Calls.Enqueue($"create:{model.Organization}.{model.Project}");

        return Task.FromResult(new MachineAccess
        {
            Id = $"client_{suffix}",
            ObjectId = $"conn_app_{suffix}",
            AccessKey = $"secret-{suffix}",
            Scope = model.Scope,
            AnnotationKey = model.AnnotationKey,
        });
    }

    public Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project)
    {
        Calls.Enqueue($"reset:{objectId}");
        return Task.FromResult<string?>($"reset-secret-{objectId}");
    }

    public Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project)
    {
        Calls.Enqueue($"delete:{objectId}");
        return Task.FromResult(true);
    }

    public Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return ResetMachineAccessAsync(existingAccess.ObjectId, organization, project);
    }

    public Task<bool> DeleteMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
    {
        return DeleteMachineAccessAsync(existingAccess.ObjectId, organization, project);
    }
}
