using System.Collections.Concurrent;

namespace _42.Platform.Cli.AutomationTests.Access;

// The AuthKit browser session of each pool user, per session (run id): the storage state (cookies on the AuthKit domain)
// of the context that last approved a device code for the user, so a later approval for the same user (a test with a
// fresh home, a session that expired) may skip the credentials on the hosted page. In memory only, never written to a
// file or an artifact. Whether the device page accepts the session without asking again is not verified yet (no AuthKit
// target in phase 3); the device page's flow enters the credentials whenever it asks for them, so a state AuthKit does
// not take costs nothing but the attempt.
public static class AuthKitSessionCache
{
    private static readonly ConcurrentDictionary<(string RunId, string User), string> States = new();

    public static string? Get(string runId, string userFingerprint)
    {
        return States.TryGetValue((runId, userFingerprint), out var state) ? state : null;
    }

    public static void Set(string runId, string userFingerprint, string storageState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageState);
        States[(runId, userFingerprint)] = storageState;
    }

    public static void Forget(string runId, string userFingerprint)
    {
        States.TryRemove((runId, userFingerprint), out _);
    }
}
