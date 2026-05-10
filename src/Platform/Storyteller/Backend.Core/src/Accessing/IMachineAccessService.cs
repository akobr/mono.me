using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

public interface IMachineAccessService
{
    Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model);

    Task<string?> ResetMachineAccessAsync(string objectId, string organization, string project);

    Task<bool> DeleteMachineAccessAsync(string objectId, string organization, string project);
}
