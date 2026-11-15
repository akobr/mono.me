using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

// Every change of access takes the caller, and writes its access audit record in the same transaction as the change
// (docs/Platform/Storyteller/access-audit.md); a change that changes nothing records nothing.
public interface IAccessService
{
    Task<Account?> GetAccountAsync(string id);

    // With an organization and a project, the account owns both, and their access points are created with it.
    Task<Account> CreateAccountAsync(AccountCreate model, AccessCaller caller);

    Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey);

    Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId);

    Task<AccessPoint?> GetAccessPointAsync(string key);

    // The owner (model.OwnerId) is not always the caller: DbCreator creates projects for an owner as System.
    Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model, AccessCaller caller);

    // Every project where the account has a role, with its description and settings (the defaults when none were
    // stored), ordered by key.
    // NotFoundException when the account doesn't exist.
    Task<IReadOnlyList<ProjectSummary>> GetProjectsAsync(string accountId)
        => throw new NotSupportedException("This access service does not support project summaries.");

    // Replaces the description of a project. NotFoundException when the project doesn't exist,
    // ArgumentException for an organization key.
    Task<AccessPoint> UpdateAccessPointAsync(string key, AccessPointUpdate model, AccessCaller caller)
        => throw new NotSupportedException("This access service does not support updating access points.");

    // The caller's account needs Administrator on the access point (Owner to grant or revoke Owner); model.CreatedById
    // is not read.
    Task<bool> GrantPermissionAsync(Permission model, AccessCaller caller);

    Task<bool> RevokePermissionAsync(Permission model, AccessCaller caller);

    // Members of an organization or project. The actor needs Administrator on the access point.
    Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId);

    // Sets the exact role of an existing member, up or down. The caller's account needs Administrator.
    Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, AccessCaller caller);

    // Removes a member. An administrator removes others; any member may remove themselves (leave).
    Task RemoveMemberAsync(string accessPointKey, string accountId, AccessCaller caller);

    // Trusted: adds the account to the access point, or raises its role, without checking the caller. Callers authorize
    // first, for example an accepted invitation, which it names in the record (detail). Returns the resulting role.
    Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role, AccessCaller caller, string? detail = null);

    Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project);

    Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id);

    Task<MachineAccess> CreateMachineAccessAsync(Model.MachineAccessCreate model, AccessCaller caller);

    // The new credential is returned only after its record is stored.
    Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId, AccessCaller caller);

    Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId, AccessCaller caller);

    Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId);

    // Replaces the federation rules of a Federated machine; the caller validates them against the project's issuers.
    // NotFoundException when the machine doesn't exist, InvalidInputException when it is not Federated.
    Task<MachineAccess> SetMachineFederationRulesAsync(string organization, string project, string id, IReadOnlyList<FederationRule> rules, AccessCaller caller)
        => throw new NotSupportedException("This access service does not support federation rules.");

    // The ids of at most max machines whose federation rules reference the trusted issuer name.
    Task<IReadOnlyList<string>> FindMachinesUsingIssuerAsync(string organization, string project, string issuerName, int max)
        => throw new NotSupportedException("This access service does not support federation rules.");
}
