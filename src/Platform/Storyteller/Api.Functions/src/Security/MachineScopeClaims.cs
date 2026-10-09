using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Api.Security;

public static class MachineScopeClaims
{
    public static IReadOnlyList<string> GetRoleClaimsForScope(MachineAccessScope scope)
    {
        return MachineAccessScopes.Get(scope);
    }
}
