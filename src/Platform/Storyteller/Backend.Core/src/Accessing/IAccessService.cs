using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

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

    // Members of an organization or project. The actor needs Administrator on the access point.
    Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId);

    // Sets the exact role of an existing member, up or down.
    Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId);

    // Removes a member. An administrator removes others; any member may remove themselves (leave).
    Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId);

    // Trusted: adds the account to the access point, or raises its role, without checking an actor.
    // Callers authorize first, for example an accepted invitation. Returns the resulting role.
    Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role);

    Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project);

    Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id);

    Task<MachineAccess> CreateMachineAccessAsync(Model.MachineAccessCreate model);

    Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId);

    Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId);

    Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId);
}
