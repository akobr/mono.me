namespace _42.Platform.Storyteller.KeyValues.Policy;

// The scheduled work of the protection levels: pending re-wraps, and the daily checks of the keys.
public interface IKeyValueMaintenanceService
{
    // Every project with a stored policy: the daily key checks, then the re-wrap in progress.
    Task RunAsync(CancellationToken cancellationToken = default);

    // Moves every envelope of the level to the current key version; returns the status, or null when the level has no key.
    Task<RewrapStatus?> RewrapAsync(string organization, string project, KeyValueProtection protection, CancellationToken cancellationToken = default);

    // The daily checks of one project: new key versions start a re-wrap, the customer's key health is recorded.
    Task<KeyValuePolicy> CheckKeysAsync(string organization, string project, bool force = false, CancellationToken cancellationToken = default);
}
