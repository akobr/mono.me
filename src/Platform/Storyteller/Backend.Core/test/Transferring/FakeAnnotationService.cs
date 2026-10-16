using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

// Only the members an export and an import use.
internal sealed class FakeAnnotationService(ExportReads? reads = null, ImportLog? imports = null) : IAnnotationService
{
    public IReadOnlyList<Annotation> Annotations { get; set; } = [];

    // The stored annotations that are not deleted, by canonical key.
    public HashSet<string> Active { get; set; } = new(StringComparer.Ordinal);

    // The keys of each existence read.
    public List<IReadOnlyList<string>> ActiveReads { get; } = [];

    // By default an active key is unchanged, and any other one is created and recorded in the overlay, as the
    // Cosmos import does.
    public Func<FullKey, JObject, ViewImportOverlay, ViewImportResult>? Import { get; set; }

    public Task<IReadOnlyList<Annotation>> GetViewAnnotationsAsync(string organization, string project, string view, CancellationToken cancellationToken = default)
    {
        reads?.Add(ViewArchiveContent.Annotations, organization, project, view);
        return Task.FromResult(Annotations);
    }

    public Task<IReadOnlySet<string>> FindActiveAnnotationKeysAsync(string organization, string project, string view, IReadOnlyCollection<AnnotationKey> keys, CancellationToken cancellationToken = default)
    {
        var requested = keys.Select(key => key.ToString()).ToList();
        ActiveReads.Add(requested);
        return Task.FromResult<IReadOnlySet<string>>(requested.Where(Active.Contains).ToHashSet(StringComparer.Ordinal));
    }

    public Task<ViewImportResult> ImportAnnotationAsync(FullKey key, JObject content, ViewImportOptions options, ViewImportOverlay overlay)
    {
        imports?.Add(ViewDocumentKind.Annotation, null, key.Annotation.ToString(), options, overlay);
        return Task.FromResult((Import ?? ImportByDefault)(key, content, overlay));
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

    private ViewImportResult ImportByDefault(FullKey key, JObject content, ViewImportOverlay overlay)
    {
        if (Active.Contains(key.Annotation.ToString()))
        {
            return new ViewImportResult(ViewImportOutcome.Unchanged);
        }

        overlay.AddActiveAnnotation(key.Annotation);
        return new ViewImportResult(ViewImportOutcome.Created);
    }
}
