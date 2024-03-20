using System.Threading.Tasks;
using _42.Platform.Storyteller.Access.Entities;
using _42.Platform.Storyteller.Access.Models;

namespace _42.Platform.Storyteller.Access;

public interface IMachineAccessService
{
    Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model);

    Task<string?> ResetMachineAccessAsync(string objectId);

    Task<bool> DeleteMachineAccessAsync(string objectId);
}
