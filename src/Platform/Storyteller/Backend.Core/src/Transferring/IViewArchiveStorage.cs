namespace _42.Platform.Storyteller.Transferring;

// The zips of exports and uploaded imports.
public interface IViewArchiveStorage
{
    // Stores the archive from the stream's current position, replacing an existing one.
    Task UploadAsync(string name, Stream content, CancellationToken cancellationToken = default);

    // Null when the archive does not exist (anymore).
    Task<Stream?> OpenReadAsync(string name, CancellationToken cancellationToken = default);

    Task DeleteAsync(string name, CancellationToken cancellationToken = default);

    // Removes archives last written before the threshold and returns how many.
    Task<int> DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken = default);
}
