namespace _42.Platform.Storyteller.Transferring;

public interface IConfigurationTransferStore
{
    // Stores a new job; the returned record carries the ETag.
    Task<ConfigurationTransferRecord> CreateAsync(ConfigurationTransferRecord record, CancellationToken cancellationToken = default);

    // A job is only found through the view it was started in.
    Task<ConfigurationTransferRecord?> GetAsync(
        string organization,
        string project,
        string view,
        ConfigurationTransferKind kind,
        string id,
        CancellationToken cancellationToken = default);

    // Replaces the job when its ETag still matches; null when another writer changed or removed it.
    Task<ConfigurationTransferRecord?> TryReplaceAsync(ConfigurationTransferRecord record, CancellationToken cancellationToken = default);

    // Queued and running jobs of the project, in every view, changed since the given time.
    Task<int> CountActiveAsync(string organization, string project, DateTimeOffset changedSince, CancellationToken cancellationToken = default);
}
