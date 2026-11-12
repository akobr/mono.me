namespace _42.Platform.Storyteller.Annotating;

// The bulk create of annotations for seeding a view with many of them (DbCreator's data sets). A separate interface, so
// the implementations and fakes of IAnnotationService stay as they are.
public interface IAnnotationBulkWriter
{
    // Creates the annotations of one view that are not active, with their missing ancestors, in waves of transactional
    // batches (roots first), with the items, stamps, audit entries and relation arrays of a create. Never changes an
    // active annotation (Unchanged when equal, Skipped DocumentExists otherwise) and never re-creates a deleted one
    // (Skipped AnnotationDeleted, with what hangs on it): the outcomes of a sequential import with Existing = Skip. The
    // project and view of the call win over those of the annotations. Re-runnable: a run after a failure completes it.
    Task<AnnotationBulkResult> CreateMissingAnnotationsAsync(
        string organization,
        string project,
        string view,
        IReadOnlyList<Annotation> annotations,
        AuditActor actor,
        AnnotationBulkOptions? options = null,
        CancellationToken cancellationToken = default);
}
