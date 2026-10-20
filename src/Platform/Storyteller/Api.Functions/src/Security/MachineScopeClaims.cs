using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Api.Security;

public static class MachineScopeClaims
{
    // One role claim per scope string: the preset's, then the management scopes granted on top of it.
    public static IReadOnlyList<string> GetRoleClaimsForScope(MachineAccessScope scope, IEnumerable<MachineManagementScope>? managementScopes = null)
    {
        return MachineAccessScopes.Get(scope, managementScopes);
    }
}
