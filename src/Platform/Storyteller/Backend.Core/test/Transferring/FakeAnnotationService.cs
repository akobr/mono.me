using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

// Only the member an export uses.
internal sealed class FakeAnnotationService(ExportReads? reads = null) : IAnnotationService
{
    public IReadOnlyList<Annotation> Annotations { get; set; } = [];

    public Task<IReadOnlyList<Annotation>> GetViewAnnotationsAsync(string organization, string project, string view, CancellationToken cancellationToken = default)
    {
        reads?.Add(ViewArchiveContent.Annotations, organization, project, view);
        return Task.FromResult(Annotations);
    }

    public Task<bool> ExistAnnotationAsync(FullKey fullKey) => throw new NotSupportedException();

    public Task<Annotation?> GetAnnotationAsync(FullKey fullKey) => throw new NotSupportedException();

    public Task<Annotation?> GetDeletedAnnotationAsync(FullKey fullKey) => throw new NotSupportedException();

    public Task<IReadOnlyList<Annotation>> FindDeletedAnnotationsAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys) => throw new NotSupportedException();

    public Task<AnnotationsResponse> GetAnnotationsAsync(AnnotationsRequest request) => throw new NotSupportedException();

    public Task<AnnotationsResponse> GetDeletedAnnotationsAsync(string organization, string project, string view, bool rootsOnly = true, string? deletionId = null, string? continuationToken = null) => throw new NotSupportedException();

    public Task<Annotation> SetAnnotationAsync(string organization, Annotation annotation, AuditActor actor) => throw new NotSupportedException();

    public Task<IEnumerable<Annotation>> CreateAnnotationAsync(string organization, Annotation annotation, AuditActor actor) => throw new NotSupportedException();

    public Task UpdateAnnotationAsync(string organization, Annotation annotation, AuditActor actor) => throw new NotSupportedException();

    public Task<IEnumerable<Annotation>> CreateAnnotationsAsync(string organization, IEnumerable<Annotation> annotations, AuditActor actor) => throw new NotSupportedException();

    public Task<IEnumerable<Annotation>> CreateAnnotationsFromStringAsync(string organization, string project, string view, IEnumerable<string> annotations, AuditActor actor) => throw new NotSupportedException();

    public Task<AnnotationDeletionResult> DeleteAnnotationAsync(FullKey fullKey, AuditActor actor) => throw new NotSupportedException();

    public Task<AnnotationRestoreResult> RestoreAnnotationsAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys, AuditActor actor, bool includeDescendants = true) => throw new NotSupportedException();

    public Task<AnnotationPurgeResult> PurgeAnnotationAsync(FullKey fullKey, AuditActor actor) => throw new NotSupportedException();

    public Task<AnnotationHistory?> GetAnnotationHistoryAsync(FullKey fullKey) => throw new NotSupportedException();

    public Task<ViewImportResult> ImportAnnotationAsync(FullKey key, JObject content, ViewImportOptions options, ViewImportOverlay overlay) => throw new NotSupportedException();
}
