namespace _42.Platform.Storyteller.KeyValues.Policy;

// The scheduled work of the protection levels: pending re-wraps, and the daily checks of the keys.
public interface IKeyValueMaintenanceService
{
    // Every project with a stored policy (a project gets one with its first Standard secret): the daily key checks,
    // then the re-wrap in progress.
    Task RunAsync(CancellationToken cancellationToken = default);

    // Moves every envelope of the level to the current key version; returns the status, or null when the level has no key.
    Task<RewrapStatus?> RewrapAsync(string organization, string project, KeyValueProtection protection, CancellationToken cancellationToken = default);

    // The daily checks of one project: new key versions start a re-wrap, the customer's key health is recorded.
    // A missing Standard key is recorded whenever the other keys were checked last.
    Task<KeyValuePolicy> CheckKeysAsync(string organization, string project, bool force = false, CancellationToken cancellationToken = default);

    // Records the project's Standard key in its policy when the policy does not know it yet (the first Standard write).
    // Returns the policy; a project without an access point has no policy to record into and is returned as it is.
    Task<KeyValuePolicy> RecordStandardKeyAsync(string organization, string project, CancellationToken cancellationToken = default);
}
