using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests.Transferring;

// The imports of a test in their order, shared by the fakes: each document as ViewArchiveNames.Describe words it,
// with the options and the overlay of the call, and what the overlay held at that moment.
internal sealed class ImportLog
{
    public List<Call> Calls { get; } = [];

    public IEnumerable<string> Documents => Calls.Select(call => call.Document);

    public void Add(ViewDocumentKind kind, string? annotationType, string? annotationKey, ViewImportOptions options, ViewImportOverlay? overlay)
    {
        Calls.Add(new Call(
            ViewArchiveNames.Describe(kind, annotationType, annotationKey),
            options,
            overlay,
            overlay is null ? null : Snapshot.Of(overlay)));
    }

    public Call Single(string document) => Calls.Single(call => call.Document == document);

    internal sealed record class Call(string Document, ViewImportOptions Options, ViewImportOverlay? Overlay, Snapshot? Seen);

    // The content of an overlay at one moment, sorted; schemas as ViewArchiveNames.Describe words them.
    internal sealed record class Snapshot(
        IReadOnlyList<string> ActiveAnnotations,
        IReadOnlyList<string> RecreatedAnnotations,
        IReadOnlyList<string> FailedAnnotations,
        IReadOnlyList<string> Schemas,
        IReadOnlyList<string> Configurations)
    {
        public static Snapshot Of(ViewImportOverlay overlay)
        {
            return new Snapshot(
                overlay.ActiveAnnotations.Order(StringComparer.Ordinal).ToList(),
                overlay.RecreatedAnnotations.Order(StringComparer.Ordinal).ToList(),
                overlay.FailedAnnotations.Order(StringComparer.Ordinal).ToList(),
                overlay.Schemas.Keys.Select(key => ViewArchiveNames.Describe(key.Kind, key.AnnotationType, key.AnnotationKey)).Order(StringComparer.Ordinal).ToList(),
                overlay.Configurations.Keys.Order(StringComparer.Ordinal).ToList());
        }
    }
}
