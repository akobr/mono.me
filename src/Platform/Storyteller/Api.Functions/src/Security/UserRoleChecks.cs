namespace _42.Platform.Storyteller.Api.Security;

// Debug builds (DEV_AUTH) skip the role checks of users. A test turns them on for its own async flow, so it runs the
// checks of a Release build in a Debug build while other tests, in parallel, keep the Debug behaviour (424-164).
// Nothing turns them off: Release builds always check, and Enforce changes nothing there.
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
