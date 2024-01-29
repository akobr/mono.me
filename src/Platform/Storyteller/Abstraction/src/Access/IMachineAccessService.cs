using System.Threading.Tasks;
using _42.Platform.Storyteller.Access.Entities;

namespace _42.Platform.Storyteller.Access;

public interface IMachineAccessService
{
    Task<bool> CreateOrganizationAsync(string organization);

    Task<MachineAccess> CreateMachineAccessAsync(string organization);

    Task<bool> DeleteMachineAccessAsync(string organization, string id);
}
