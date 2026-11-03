namespace _42.Platform.Storyteller.Sdk.Configuring;

// Configurations on demand, for a job that reads them while it runs: services.AddStorytellerDocuments(…). Every call
// revalidates with If-None-Match, so it sees the platform's current document; a 304 returns the document from memory.
// Nothing is written to disk, and there is no last-known-good: a failure throws StorytellerConfigurationException.
public interface IStorytellerDocuments
{
    // In the mode of the options (STORYTELLER_MODE, Resolved when unset). key is a full key, or an annotation key in the
    // organization, project and view of the options' Key.
    Task<StorytellerDocument> GetAsync(string key, CancellationToken cancellationToken = default);

    Task<StorytellerDocument> GetAsync(string key, StorytellerReadMode mode, CancellationToken cancellationToken = default);
}
