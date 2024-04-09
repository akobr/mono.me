using _42.Platform.Storyteller.Access;
using _42.Platform.Storyteller.Backend.Accessing.Model;

namespace _42.Platform.Storyteller.Backend.Accessing;

public interface IAccessService
{
    Task<Account?> GetAccountAsync(string id);

    Task<Account> CreateAccountAsync(AccountCreate model);

    Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey);

    Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId);

    Task<AccessPoint?> GetAccessPointAsync(string key);

    Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model);

    Task<bool> GrantPermissionAsync(Permission model);

    Task<bool> RevokePermissionAsync(Permission model);

    Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project);

    Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id);

    Task<MachineAccess> CreateMachineAccessAsync(Model.MachineAccessCreate model);

    Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId);

    Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId);

    Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId);
}
