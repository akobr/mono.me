using _42.Platform.Storyteller.Access;
using _42.Platform.Storyteller.Backend.Accessing.Model;

namespace _42.Platform.Storyteller.Backend.Accessing;

public interface IMachineAccessService
{
    Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model);

    Task<string?> ResetMachineAccessAsync(string objectId);

    Task<bool> DeleteMachineAccessAsync(string objectId);
}
