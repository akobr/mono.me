using _42.Platform.Storyteller.Accessing;

namespace _42.Platform.Storyteller.Api.Security;

public static class MachineScopeClaims
{
    // One role claim per scope string of the stored list; a record without scopes gives none.
    public static IReadOnlyList<string> GetRoleClaims(IEnumerable<MachineScope>? scopes)
    {
        return scopes is null ? [] : MachineScopes.ToScopeStrings(scopes);
    }
}
