using System.Runtime.CompilerServices;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Transferring;

// The passes of an import (1.15), on the services of the four kinds: the annotations are imported (ancestors first),
// the overlay is filled, and the schemas, templates and configurations follow. A file that failed while reading is
// reported in the pass of its kind.
public sealed class ViewImporter : IViewImporter
{
    private readonly IAnnotationService _annotations;
    private readonly IConfigurationSchemaService _schemas;
    private readonly IConfigurationTemplateService _templates;
    private readonly IConfigurationService _configurations;
    private readonly ILogger _logger;

    public ViewImporter(
        IAnnotationService annotations,
        IConfigurationSchemaService schemas,
        IConfigurationTemplateService templates,
        IConfigurationService configurations,
        ILogger<ViewImporter> logger)
        : this(annotations, schemas, templates, configurations, (ILogger)logger)
    {
    }

    // The import jobs log through their own logger; the container sees the public constructor only.
    internal ViewImporter(
        IAnnotationService annotations,
        IConfigurationSchemaService schemas,
        IConfigurationTemplateService templates,
        IConfigurationService configurations,
        ILogger logger)
    {
        _annotations = annotations;
        _schemas = schemas;
        _templates = templates;
        _configurations = configurations;
        _logger = logger;
    }

    public async IAsyncEnumerable<ViewImportFileResult> ImportAsync(
        string organization,
        string project,
        string view,
        IReadOnlyList<ViewArchiveFile> files,
        ViewImportOptions options,
        ViewImportOverlay? overlay = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var ordered = OrderByPass(files);
        var stored = await ReadStoredAsync(organization, project, view, ordered, options, cancellationToken);
        overlay ??= new ViewImportOverlay();
        var isOverlayFilled = false;

        foreach (var file in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // after the annotation pass, the keys that the import makes active are known
            if (!isOverlayFilled && file.Kind != ViewDocumentKind.Annotation)
            {
                FillOverlay(ordered, stored, options, overlay);
                isOverlayFilled = true;
            }

            var result = await ImportFileOrConflictAsync(organization, project, view, file, options, overlay);
            Track(file, result, overlay);
            yield return new ViewImportFileResult(file, result);
        }
    }

    // The files in the passes of an import (Order): annotations by depth in the graph, type and key, so ancestors come
    // first; type, descendant-type and annotation schemas, each by type and key; templates by type; configurations by
    // key. A duplicate follows the file that holds its document (the first path).
    private static List<ViewArchiveFile> OrderByPass(IReadOnlyList<ViewArchiveFile> files)
    {
        return files
            .OrderBy(file => ViewArchiveNames.GetContent(file.Kind))
            .ThenBy(file => file.Kind)
            .ThenBy(file => file.Kind == ViewDocumentKind.Annotation ? file.AnnotationKey!.GetDepth() : 0)
            .ThenBy(file => file.Kind == ViewDocumentKind.Annotation ? file.AnnotationKey!.Type : default)
            .ThenBy(file => file.AnnotationType, StringComparer.Ordinal)
            .ThenBy(file => file.AnnotationKey?.ToString(), StringComparer.Ordinal)
            .ThenBy(file => file.Path, StringComparer.Ordinal)
            .ToList();
    }

    // A schema or configuration file that the import may write: read without an error, and not empty.
    private static bool IsOverlayCandidate(ViewArchiveFile file)
    {
        return file.ErrorCode is null
               && file.Content is { HasValues: true }
               && file.Kind is not (ViewDocumentKind.Annotation or ViewDocumentKind.Template);
    }

    // The schemas and configurations of the archive that the import will write (1.10), once the annotation pass is
    // done: not empty, of an annotation that is active (stored, or made active by the import), and allowed by
    // Existing. With Skip, a document the view already has stays, except the configuration of a re-created
    // annotation, which the re-create emptied. Whether a schema is a valid JSON Schema is left to the schema service,
    // which ignores the invalid layers of the overlay.
    private static void FillOverlay(IEnumerable<ViewArchiveFile> files, StoredDocuments stored, ViewImportOptions options, ViewImportOverlay overlay)
    {
        var isSkip = options.Existing == ViewImportExisting.Skip;

        foreach (var file in files.Where(IsOverlayCandidate))
        {
            var key = file.AnnotationKey?.ToString();
            var hasAnnotation = key is not null && (stored.ActiveAnnotations.Contains(key) || overlay.ActiveAnnotations.Contains(key));

            if (file.Kind == ViewDocumentKind.Configuration)
            {
                if (hasAnnotation
                    && (!isSkip || !stored.Configurations.Contains(key!) || overlay.RecreatedAnnotations.Contains(key!)))
                {
                    overlay.SetConfiguration(file.AnnotationKey!, file.Content!);
                }

                continue;
            }

            var schemaKey = GetSchemaKey(file);

            if ((file.Kind == ViewDocumentKind.TypeSchema || hasAnnotation)
                && (!isSkip || !stored.Schemas.Contains(schemaKey)))
            {
                overlay.SetSchema(schemaKey, file.Content!);
            }
        }
    }

    // The overlay follows the outcomes. An annotation that fails, or is skipped for a failed ancestor, fails what hangs
    // on it. A schema or configuration that fails or is skipped is not written, so it leaves the overlay. A duplicate
    // file changes nothing: its document is the one of the first file.
    private static void Track(ViewArchiveFile file, ViewImportResult result, ViewImportOverlay overlay)
    {
        if (file.ErrorCode == ErrorCodes.DuplicateFile)
        {
            return;
        }

        switch (file.Kind)
        {
            case ViewDocumentKind.Annotation:
                if (result.Outcome == ViewImportOutcome.Failed || result.ErrorCode == ErrorCodes.DependencyFailed)
                {
                    overlay.AddFailedAnnotation(file.AnnotationKey!);
                }

                break;

            case ViewDocumentKind.Template:
                break;

            case ViewDocumentKind.Configuration:
                if (result.Outcome is ViewImportOutcome.Failed or ViewImportOutcome.Skipped)
                {
                    overlay.RemoveConfiguration(file.AnnotationKey!);
                }

                break;

            default:
                if (result.Outcome is ViewImportOutcome.Failed or ViewImportOutcome.Skipped)
                {
                    overlay.RemoveSchema(GetSchemaKey(file));
                }

                break;
        }
    }

    private static SchemaKey GetSchemaKey(ViewArchiveFile file)
    {
        return file.Kind switch
        {
            ViewDocumentKind.TypeSchema => SchemaKey.ForType(file.AnnotationType!),
            ViewDocumentKind.AnnotationSchema => SchemaKey.ForAnnotation(file.AnnotationKey!.ToString()),
            _ => SchemaKey.ForDescendantType(file.AnnotationKey!.ToString(), file.AnnotationType!),
        };
    }

    private static bool IsWriteConflict(Exception exception)
    {
        return exception is SchemaConcurrencyException or TemplateConcurrencyException
            || (exception is ConflictException conflict && conflict.ErrorCode == ErrorCodes.Conflict);
    }

    // What the overlay needs to know of the view before the annotation pass (1.10), with at most one read per kind:
    // which annotations of the schema and configuration files are active, and, with Skip, which schemas and
    // configurations the view already has. Templates are not in the overlay, and an annotation import reads its key.
    private async Task<StoredDocuments> ReadStoredAsync(
        string organization,
        string project,
        string view,
        IReadOnlyList<ViewArchiveFile> files,
        ViewImportOptions options,
        CancellationToken cancellationToken)
    {
        var candidates = files.Where(IsOverlayCandidate).ToList();
        var keys = candidates
            .Where(file => file.Kind != ViewDocumentKind.TypeSchema)
            .Select(file => file.AnnotationKey!)
            .DistinctBy(key => key.ToString(), StringComparer.Ordinal)
            .ToList();

        var annotations = keys.Count > 0
            ? await _annotations.FindActiveAnnotationKeysAsync(organization, project, view, keys, cancellationToken)
            : new HashSet<string>();

        var isSkip = options.Existing == ViewImportExisting.Skip;
        var schemas = isSkip && candidates.Any(file => file.Kind != ViewDocumentKind.Configuration)
            ? await _schemas.GetSchemaKeysAsync(organization, project, view, cancellationToken)
            : new HashSet<SchemaKey>();
        var configurations = isSkip && candidates.Any(file => file.Kind == ViewDocumentKind.Configuration)
            ? await ReadConfigurationKeysAsync(organization, project, view, cancellationToken)
            : new HashSet<string>();

        return new StoredDocuments(annotations, schemas, configurations);
    }

    // The annotations of the view whose own document is not empty, through the pages of the configuration listing.
    private async Task<IReadOnlySet<string>> ReadConfigurationKeysAsync(string organization, string project, string view, CancellationToken cancellationToken)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        string? continuationToken = null;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = await _configurations.ListConfigurationsAsync(organization, project, view, continuationToken: continuationToken);
            keys.UnionWith(page.Configurations.Where(configuration => configuration.HasContent).Select(configuration => configuration.AnnotationKey));
            continuationToken = page.ContinuationToken;
        }
        while (!string.IsNullOrEmpty(continuationToken));

        return keys;
    }

    // A write that keeps hitting concurrent changes after its own retries fails its file, not the whole import: the
    // overlay then treats the file as failed, so the documents that depend on it are skipped with DependencyFailed.
    private async Task<ViewImportResult> ImportFileOrConflictAsync(
        string organization,
        string project,
        string view,
        ViewArchiveFile file,
        ViewImportOptions options,
        ViewImportOverlay overlay)
    {
        try
        {
            return await ImportFileAsync(organization, project, view, file, options, overlay);
        }
        catch (Exception exception) when (IsWriteConflict(exception))
        {
            _logger.LogWarning(exception, "The file {Path} of the import into {Organization}.{Project}.{View} failed on concurrent changes.", file.Path, organization, project, view);
            return new ViewImportResult(
                ViewImportOutcome.Failed,
                ErrorCode: ErrorCodes.Conflict,
                Message: $"The document was changed concurrently while it was imported; import the file again. {exception.Message}");
        }
    }

    private Task<ViewImportResult> ImportFileAsync(
        string organization,
        string project,
        string view,
        ViewArchiveFile file,
        ViewImportOptions options,
        ViewImportOverlay overlay)
    {
        if (file.ErrorCode is not null)
        {
            return Task.FromResult(new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: file.ErrorCode, Message: file.Message));
        }

        return file.Kind switch
        {
            ViewDocumentKind.Annotation => _annotations.ImportAnnotationAsync(
                FullKey.Create(file.AnnotationKey!, organization, project, view),
                file.Content!,
                options,
                overlay),
            ViewDocumentKind.Template => _templates.ImportTemplateAsync(organization, project, view, file.AnnotationType!, file.Content!, options),
            ViewDocumentKind.Configuration => _configurations.ImportConfigurationAsync(
                FullKey.Create(file.AnnotationKey!, organization, project, view),
                file.Content!,
                options,
                overlay),
            _ => _schemas.ImportSchemaAsync(
                new ViewSchemaTarget(organization, project, view, file.Kind, file.AnnotationType, file.AnnotationKey?.ToString()),
                file.Content!,
                options,
                overlay),
        };
    }

    // The stored documents an import needs to know of before its annotation pass, by canonical keys.
    private sealed record class StoredDocuments(
        IReadOnlySet<string> ActiveAnnotations,
        IReadOnlySet<SchemaKey> Schemas,
        IReadOnlySet<string> Configurations);
}
