using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface IMachineAccessService
{
    Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model);

    Task<MachineAccess> ExtendMachineAccessAsync(MachineAccess existingAccess, MachineAccessCreate model)
        => throw new NotSupportedException("This machine access service does not support extension.");

    Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project);

    Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project);

    // Overloads with the stored access, so a router can follow the credential kind it was created with
    // rather than the current project policy. The defaults keep the identifiers callers passed before.
    Task<string?> ResetMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
        => ResetMachineAccessAsync(existingAccess.Id, organization, project);

    Task<bool> DeleteMachineAccessAsync(MachineAccess existingAccess, string organization, string project)
        => DeleteMachineAccessAsync(existingAccess.ObjectId, organization, project);
}
