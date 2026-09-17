using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface IMachineAccessService
{
    Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model);

    Task<MachineAccess> ExtendMachineAccessAsync(MachineAccess existingAccess, MachineAccessCreate model)
        => throw new NotSupportedException("This machine access service does not support extension.");

    Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project);

    Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project);
}
