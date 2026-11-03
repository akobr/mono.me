namespace _42.Platform.Storyteller.Api.Security;

// Debug builds (DEV_AUTH) skip the role check of a user on the access point of a route (CheckUserAccessAsync, also for
// the role that the scopes of a machine need). A test turns it on for its own async flow, so that check runs as in a
// Release build while other tests, in parallel, keep the Debug behaviour (424-164). The other DEV_AUTH branches of users
// stay: User.Impersonation, and the secret rights every user gets. Nothing turns the check off: Release builds always
// check, and Enforce changes nothing there.
internal static class UserRoleChecks
{
#if DEV_AUTH
    private static readonly AsyncLocal<bool> EnforcedInFlow = new();

    public static bool AreSkipped => !EnforcedInFlow.Value;

    // Until the scope is disposed; the flows started from this one inherit it.
    public static IDisposable Enforce()
    {
        var previous = EnforcedInFlow.Value;
        EnforcedInFlow.Value = true;
        return new Scope(() => EnforcedInFlow.Value = previous);
    }
#else
    public static bool AreSkipped => false;

    public static IDisposable Enforce() => new Scope(static () => { });
#endif

    private sealed class Scope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }
}
