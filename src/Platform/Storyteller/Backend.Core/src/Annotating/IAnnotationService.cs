using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Annotating;

public interface IAnnotationService
{
    // Annotations that are not deleted, whatever their status (AnnotationStatus): a soft-deleted annotation does not
    // exist for these reads, a disabled, scheduled or expired one does.
    Task<bool> ExistAnnotationAsync(FullKey fullKey);

    Task<Annotation?> GetAnnotationAsync(FullKey fullKey);

    // Deleted annotations only, with their Deletion.
    Task<Annotation?> GetDeletedAnnotationAsync(FullKey fullKey);

    // The deleted annotations among the keys (the lookup before a create). One read of many items, no query.
    Task<IReadOnlyList<Annotation>> FindDeletedAnnotationsAsync(
        string organization,
        string project,
        string view,
        IReadOnlyCollection<AnnotationKey> keys);

    // Only the annotations that are not deleted and active now, unless the request includes the others
    // (AnnotationsRequest.Deleted and IncludeInactive). A page can hold fewer items than the page size.
    Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request);

    // Every annotation of a view that is not deleted, of every type and status, with its Created and Modified stamps
    // (an export). Values are read as the point reads read them (System.Text.Json, JsonElement): date strings stay
    // strings, and numbers keep the text Cosmos DB answers.
    Task<IReadOnlyList<Annotation>> GetViewAnnotationsAsync(
        string organization,
        string project,
        string view,
        CancellationToken cancellationToken = default);

    // The deleted annotations of a view, newest deletion first, with their Deletion. One query over every partition
    // of the project, up to 1000 per page.
    Task<AnnotationsResponse> GetDeletedAnnotationsAsync(
        string organization,
        string project,
        string view,
        bool rootsOnly = true,
        string? deletionId = null,
        string? continuationToken = null);

    // Creates, updates, or re-creates a deleted key. A body equal to the stored active annotation writes nothing.
    // Returns the stored annotation.
    Task<Annotation> SetAnnotationAsync(string organization, Annotation annotation, AuditActor actor);

    // Creates the annotation and its missing ancestors; a deleted key or ancestor is re-created.
    Task<IEnumerable<Annotation>> CreateAnnotationAsync(string organization, Annotation annotation, AuditActor actor);

    Task UpdateAnnotationAsync(string organization, Annotation annotation, AuditActor actor);

    // Creates the annotations that are not active yet; active ones are skipped.
    Task<IEnumerable<Annotation>> CreateAnnotationsAsync(string organization, IEnumerable<Annotation> annotations, AuditActor actor);

    // Creates annotations from compact lines (keys, or names read by their count) in the given project and view.
    Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, string project, string view, IEnumerable<string> annotations, AuditActor actor);

    // Soft delete of the annotation and all its descendants.
    Task<AnnotationDeletionResult> DeleteAnnotationAsync(FullKey fullKey, AuditActor actor);

    Task<AnnotationRestoreResult> RestoreAnnotationsAsync(
        string organization,
        string project,
        string view,
        IReadOnlyCollection<AnnotationKey> keys,
        AuditActor actor,
        bool includeDescendants = true);

    // Permanent delete of a deleted annotation, its descendants and their audits.
    Task<AnnotationPurgeResult> PurgeAnnotationAsync(FullKey fullKey, AuditActor actor);

    Task<AnnotationHistory?> GetAnnotationHistoryAsync(FullKey fullKey);

    // The keys among the given ones whose annotation is not deleted, whatever its status, as canonical strings
    // (AnnotationKey.ToString()). One read of many items, no query (the existence check before an import).
    Task<IReadOnlySet<string>> FindActiveAnnotationKeysAsync(
        string organization,
        string project,
        string view,
        IReadOnlyCollection<AnnotationKey> keys,
        CancellationToken cancellationToken = default);

    // Imports the file of an annotation (ViewAnnotationDocument): the content replaces the one of an active key, and
    // a missing key is created with its missing ancestors. A deleted key or ancestor is re-created with Overwrite and
    // skipped (AnnotationDeleted) with Skip. An ancestor that failed in this import skips the file (DependencyFailed).
    // The writes are the ones of SetAnnotationAsync, with their audit entries by the options' actor. The keys it
    // makes active, and the re-created ones among them, are recorded in the overlay, also in a dry run.
    Task<ViewImportResult> ImportAnnotationAsync(FullKey key, JObject content, ViewImportOptions options, ViewImportOverlay overlay);
}
