using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Api.Functions.UnitTests;

// Machines kept per project and one role for every account, with the federation members of IAccessService.
internal sealed class FederationAccessService : IAccessService
{
    private readonly Dictionary<string, MachineAccess> _machines = new(StringComparer.Ordinal);

    public AccountRole Role { get; set; } = AccountRole.Owner;

    public List<string> Calls { get; } = [];

    public Dictionary<string, Account> Accounts { get; } = new(StringComparer.Ordinal);

    // What CreateMachineAccessAsync returns; the router's work is not part of these tests.
    public MachineAccess? Created { get; set; }

    public MachineAccess Add(string organization, string project, MachineAccess machine)
    {
        _machines[Key(organization, project, machine.Id)] = machine;
        return machine;
    }

    public MachineAccess? Find(string organization, string project, string id)
    {
        return _machines.GetValueOrDefault(Key(organization, project, id));
    }

    public Task<AccountRole> GetAccountRoleAsync(string accountId, string accessPointKey)
    {
        Calls.Add($"role:{accessPointKey}");
        return Task.FromResult(Role);
    }

    public Task<MachineAccess?> GetMachineAccessAsync(string organization, string project, string id)
    {
        Calls.Add($"get:{organization}/{project}/{id}");
        return Task.FromResult(Find(organization, project, id));
    }

    public Task<IEnumerable<MachineAccess>> GetMachineAccessesAsync(string organization, string project)
    {
        Calls.Add($"list:{organization}/{project}");
        var prefix = Key(organization, project, string.Empty);
        return Task.FromResult<IEnumerable<MachineAccess>>(_machines.Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal)).Select(entry => entry.Value).ToList());
    }

    public Task<MachineAccess> CreateMachineAccessAsync(MachineAccessCreate model)
    {
        Calls.Add($"create:{model.Organization}/{model.Project}");
        return Task.FromResult(Created ?? throw new InvalidOperationException("Set Created first."));
    }

    public Task<bool> VerifyAccessForMachineAsync(string organization, string project, string appId)
    {
        Calls.Add($"verify:{organization}/{project}/{appId}");
        return Task.FromResult(Find(organization, project, appId) is not null);
    }

    public Task<MachineAccess> SetMachineFederationRulesAsync(string organization, string project, string id, IReadOnlyList<FederationRule> rules)
    {
        Calls.Add($"rules:{organization}/{project}/{id}");
        var machine = Find(organization, project, id) ?? throw new NotFoundException($"The machine {id} doesn't exist.");

        if (machine.CredentialKind != MachineCredentialKind.Federated)
        {
            throw new InvalidInputException("Only Federated machines have federation rules.", ErrorCodes.MachineNotFederated);
        }

        return Task.FromResult(Add(organization, project, machine with { FederationRules = rules.ToList() }));
    }

    public Task<IReadOnlyList<string>> FindMachinesUsingIssuerAsync(string organization, string project, string issuerName, int max)
    {
        Calls.Add($"using:{organization}/{project}/{issuerName}");
        var prefix = Key(organization, project, string.Empty);
        IReadOnlyList<string> ids = _machines
            .Where(entry => entry.Key.StartsWith(prefix, StringComparison.Ordinal)
                && entry.Value.FederationRules?.Any(rule => rule.Issuer == issuerName) == true)
            .Select(entry => entry.Value.Id)
            .Take(max)
            .ToList();
        return Task.FromResult(ids);
    }

    public Task<MachineAccess> ResetMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

    public Task<bool> DeleteMachineAccessAsync(string organization, string project, string appId) => throw new NotImplementedException();

    // The accounts the user token exchange finds.
    public Task<Account?> GetAccountAsync(string id) => Task.FromResult(Accounts.GetValueOrDefault(id));

    public Task<Account> CreateAccountAsync(AccountCreate model) => throw new NotImplementedException();

    public Task<IEnumerable<AccessPoint>> GetAccessPointsAsync(string accountId) => throw new NotImplementedException();

    public Task<AccessPoint?> GetAccessPointAsync(string key) => throw new NotImplementedException();

    public Task<AccessPoint> CreateAccessPointAsync(AccessPointCreate model) => throw new NotImplementedException();

    public Task<bool> GrantPermissionAsync(Permission model) => throw new NotImplementedException();

    public Task<bool> RevokePermissionAsync(Permission model) => throw new NotImplementedException();

    public Task<IReadOnlyList<AccessPointMember>> GetMembersAsync(string accessPointKey, string actorId) => throw new NotImplementedException();

    public Task<AccessPointMember> SetMemberRoleAsync(string accessPointKey, string accountId, AccountRole role, string actorId) => throw new NotImplementedException();

    public Task RemoveMemberAsync(string accessPointKey, string accountId, string actorId) => throw new NotImplementedException();

    public Task<AccountRole> JoinAccessPointAsync(string accessPointKey, string accountId, AccountRole role) => throw new NotImplementedException();

    private static string Key(string organization, string project, string id) => $"{organization}/{project}/{id}";
}
