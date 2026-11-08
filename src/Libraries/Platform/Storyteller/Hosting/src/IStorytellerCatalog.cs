namespace _42.Platform.Storyteller.Sdk.Hosting;

// The annotations of the platform, of any type: one by its key, and the descendants of an annotation by a
// StorytellerTarget, as a list or as a stream of changes. Snapshots come from point reads, as list values are lossy. A
// key is a full key, or an annotation key in the organization, project and view of the options' Key; without that Key,
// an annotation key throws StorytellerConfigurationException naming STORYTELLER_KEY. Needs Annotation.Read; a machine
// restricted to an annotation reads that annotation and its descendants only, so it can target nothing above it.
public interface IStorytellerCatalog
{
    // One annotation, in any status; null when it does not exist or is deleted. Its snapshot is of its type, such as
    // StorytellerUnit for a unt key.
    Task<StorytellerAnnotation?> GetAsync(string key, CancellationToken cancellationToken = default);

    // The same, typed: a key of another type than TAnnotation's throws ArgumentException before any read.
    // StorytellerAnnotation takes a key of any type.
    Task<TAnnotation?> GetAsync<TAnnotation>(string key, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation;

    // The descendants of the target that are active (or inactive too, with IncludingInactive()), by key, ordinally.
    // Reads the ancestor, every page of its list, and each descendant that passes the filters, at most 4 reads at once.
    // An ancestor that does not exist throws a definitive StorytellerConfigurationException with status 404, as its list
    // would be empty; any other failure throws its StorytellerConfigurationException.
    Task<IReadOnlyList<TAnnotation>> ListAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation;

    // The informer of a target: Added for every descendant of the first complete poll, then Added, Changed and Removed
    // after every poll, Removed first, then Changed, then Added, each by key. Changed follows Modified, which a new
    // descendant of a descendant does not move, so watch that type itself to follow it. A failed poll emits nothing,
    // logs a warning and polls again; with a zero interval the one poll's failure is thrown. Each enumeration runs its
    // own loop. pollInterval: 30 s when null, with a jitter of ±10 %, counted from the end of a poll; zero polls once;
    // otherwise 1 second to 45 days, or the call throws ArgumentOutOfRangeException. Without the options' Key, an
    // annotation key throws StorytellerConfigurationException at the call. Ends with OperationCanceledException when the
    // token is cancelled.
    IAsyncEnumerable<StorytellerAnnotationChange<TAnnotation>> WatchAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation;
}
