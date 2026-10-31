using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Testing.UI.Automation.Runtime;

public sealed class TestScopeOptions
{
    // The user the test leases from the pool for its duration.
    public UserRequirement User { get; set; } = UserRequirement.Any;

    // False for a test that needs no user at all (e.g. a public page). A leased user also bounds the parallelism.
    public bool LeaseUser { get; set; } = true;

    // Start the context signed in as the user (cached storage state); false for a test that signs in itself.
    // Ignored for Target:SignIn None; needs LeaseUser.
    public bool SignedIn { get; set; } = true;

    // 1-based; the engine adapter passes its retry attempt + 1.
    public int Attempt { get; set; } = 1;

    // When the engine started the test; the scope's creation when unset.
    public DateTimeOffset? StartedAt { get; set; }
}
