using System.Runtime.CompilerServices;
using _42.Platform.Storyteller.Transferring;

namespace _42.Platform.Storyteller.DbCreator.UnitTests.DataSets;

// An importer that makes every file Created, fails the annotation keys it is told to, and tracks the overlay as the
// real one does for annotations and configurations; it records each call with what the overlay held when it started.
internal sealed class FakeViewImporter : IViewImporter
{
    public HashSet<string> FailingKeys { get; } = new(StringComparer.Ordinal);

    public List<ImporterCall> Calls { get; } = [];

    public async IAsyncEnumerable<ViewImportFileResult> ImportAsync(
        string organization,
        string project,
        string view,
        IReadOnlyList<ViewArchiveFile> files,
        ViewImportOptions options,
        ViewImportOverlay? overlay = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        overlay ??= new ViewImportOverlay();
        Calls.Add(new ImporterCall(
            organization,
            project,
            view,
            files,
            options,
            overlay,
            overlay.ActiveAnnotations.ToHashSet(),
            overlay.FailedAnnotations.ToHashSet(),
            overlay.Configurations.Keys.ToHashSet()));

        foreach (var file in files)
        {
            await Task.Yield();
            var key = file.AnnotationKey?.ToString();
            var result = file.ErrorCode is not null
                ? new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: file.ErrorCode, Message: file.Message)
                : key is not null && FailingKeys.Contains(key)
                    ? new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.InvalidAnnotation, Message: "Told to fail.")
                    : new ViewImportResult(ViewImportOutcome.Created);

            if (file.Kind == ViewDocumentKind.Annotation)
            {
                if (result.Outcome == ViewImportOutcome.Failed)
                {
                    overlay.AddFailedAnnotation(file.AnnotationKey!);
                }
                else
                {
                    overlay.AddActiveAnnotation(file.AnnotationKey!);
                }
            }
            else if (file.Kind == ViewDocumentKind.Configuration && result.Outcome == ViewImportOutcome.Created)
            {
                overlay.SetConfiguration(file.AnnotationKey!, file.Content!);
            }

            yield return new ViewImportFileResult(file, result);
        }
    }
}
