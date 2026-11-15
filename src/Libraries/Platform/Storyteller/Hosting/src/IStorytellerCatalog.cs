using _42.Platform.Storyteller.Sdk.Hosting.Catalog;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// The annotations of the platform, of any type: one by its key, alone or with its ancestors, and the descendants of an
// annotation by a StorytellerTarget, as a list, as a list with their ancestors, or as a stream of changes. Snapshots
// come from point reads, as list values are lossy. A key is a full key, or an annotation key in the organization,
// project and view of the options' Key; without that Key, an annotation key throws StorytellerConfigurationException
// naming STORYTELLER_KEY. Needs Annotation.Read; a machine restricted to an annotation reads that annotation and its
// descendants only, so it can target nothing above it, and any other ancestor of what it reads is unreadable to it, as
// the subjects and contexts of a responsibility's executions are. GetWithAncestorsAsync and ListWithAncestorsAsync
// have default implementations over the other members, so a test double of an application compiles without them;
// those know no restrictions.
public interface IStorytellerCatalog
{
    // One annotation, in any status; null when it does not exist or is deleted. Its snapshot is of its type, such as
    // StorytellerUnit for a unt key.
    Task<StorytellerAnnotation?> GetAsync(string key, CancellationToken cancellationToken = default);

    // The same, typed: a key of another type than TAnnotation's throws ArgumentException before any read.
    // StorytellerAnnotation takes a key of any type; a type of the application that derives from it names no
    // annotation type and throws ArgumentException.
    Task<TAnnotation?> GetAsync<TAnnotation>(string key, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation;

    // Who am I for any key, read on every call: the annotation of the key and its ancestors, as who am I reads them;
    // null when the key does not exist or is deleted. The keys and the failures are those of GetAsync. The ancestors
    // are read at once, in any status: one the machine may not read goes to Unreadable, a missing one is left out, and
    // any other failure throws. ReadAt is when the read started. The default implementation, which a test double gets,
    // reads the key and each ancestor with GetAsync, the ancestors by their full keys: GetAsync throws on a 403, so
    // nothing lands in Unreadable, and ReadAt is of the system clock. The full keys come from the Key of the snapshot
    // that GetAsync returned, so a snapshot whose Key is not a full key throws InvalidOperationException.
    Task<StorytellerExecutionContext?> GetWithAncestorsAsync(string key, CancellationToken cancellationToken = default)
    {
        return CatalogDefaults.GetWithAncestorsAsync(this, key, cancellationToken);
    }

    // The descendants of the target that are active (or inactive too, with IncludingInactive()), by key, ordinally.
    // Reads the ancestor, every page of its list, and each descendant that passes the filters, at most 4 reads at once.
    // An ancestor that does not exist throws a definitive StorytellerConfigurationException with status 404, as its list
    // would be empty; any other failure throws its StorytellerConfigurationException. A target that requires active
    // ancestors also reads each distinct ancestor of the descendants once, and keeps the descendants whose ancestors
    // are all active: a missing ancestor leaves its descendants out, and one the machine may not read throws a
    // definitive StorytellerConfigurationException with status 403.
    Task<IReadOnlyList<TAnnotation>> ListAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation;

    // ListAsync, and who am I of each descendant: its annotation and its ancestors, by key, ordinally. Each distinct
    // ancestor is read once, at most 4 reads at once, so one call gives every descendant with the status and the time
    // zone of its chain. An ancestor the machine may not read goes to Unreadable of each context that has it, unless
    // the target requires active ancestors, which throws as ListAsync does. ReadAt is when the call started, the same
    // in every context. The default implementation, which a test double gets, builds on ListAsync and GetAsync, and
    // keeps the descendants under active ancestors itself: nothing lands in Unreadable, and its times are of the
    // system clock.
    Task<IReadOnlyList<StorytellerExecutionContext>> ListWithAncestorsAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation
    {
        return CatalogDefaults.ListWithAncestorsAsync(this, target, cancellationToken);
    }

    // The informer of a target: Added for every descendant of the first complete poll, then Added, Changed and Removed
    // after every poll, Removed first, then Changed, then Added, each by key. Changed follows Modified, which a new
    // descendant of a descendant does not move, so watch that type itself to follow it. A failed poll emits nothing,
    // logs a warning and polls again; with a zero interval the one poll's failure is thrown, and so is a definitive
    // failure before any poll completed (an ancestor that does not exist, a 401 or a 403). A target that requires active
    // ancestors also reads each distinct ancestor of the held and of the listed descendants once in every poll: a
    // descendant is Added only under active ancestors, and a held one whose ancestor is inactive or gone is Removed with
    // that ancestor's key and its reason, the nearest one when several are; its own end is named before an ancestor's.
    // A failed read of an ancestor logs a warning and keeps the descendants under it as they are; a definitive one
    // before any poll completed, of an ancestor the machine may not read too, is thrown. Each enumeration runs its
    // own loop. pollInterval: 30 s when null, with a jitter of ±10 %, counted from the end of a poll; zero polls once;
    // otherwise 1 second to 45 days, or the call throws ArgumentOutOfRangeException. Without the options' Key, an
    // annotation key throws StorytellerConfigurationException at the call. Ends with OperationCanceledException when the
    // token is cancelled.
    IAsyncEnumerable<StorytellerAnnotationChange<TAnnotation>> WatchAsync<TAnnotation>(StorytellerTarget<TAnnotation> target, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
        where TAnnotation : StorytellerAnnotation;
}
