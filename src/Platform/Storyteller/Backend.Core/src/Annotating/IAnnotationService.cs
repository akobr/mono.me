namespace _42.Platform.Storyteller.Annotating;

public interface IAnnotationService
{
    // Active annotations only: a soft-deleted annotation does not exist for these reads.
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

    Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request);

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

    Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, IEnumerable<string> annotations, AuditActor actor);

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
}
