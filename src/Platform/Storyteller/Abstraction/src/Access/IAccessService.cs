using System.Collections.Generic;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Access.Entities;
using _42.Platform.Storyteller.Access.Models;

namespace _42.Platform.Storyteller.Access;

public interface IAccessService
{
    Task<Account?> GetAccountAsync(string key);

    Task<Account?> CreateAccountAsync(AccountCreate model);

    Task<AccountRole> GetAccountRoleAsync(string accountKey, string accessPointKey);

    Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountKey);

    Task<AccessPoint?> GetAccessPointAsync(string key);

    Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model);

    Task<bool> GrantPermissionAsync(Permission model);

    Task<bool> RevokePermissionAsync(Permission model);

    Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project);

    Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id);

    Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model);

    Task<bool> DeleteMachineAccessAsync(string organization, string project, string id);
}
