using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Transferring;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Annotating;

// The import of an annotation file. It writes the way SetAnnotationAsync does: an active key is updated
// (UpdateCoreAsync), a missing or deleted one is created or re-created with its ancestors (CreateCoreAsync), with the
// usual audit entries. The outcome and the warnings are decided from the states read before the write, so a dry run
// reports what the real run does.
public partial class CosmosAnnotationService
{
    public async Task<ViewImportResult> ImportAnnotationAsync(FullKey key, JObject content, ViewImportOptions options, ViewImportOverlay overlay)
    {
        if (!ViewAnnotationDocument.TryRead(content, key, out var annotation, out var errorCode, out var message))
        {
            return new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: errorCode, Message: message);
        }

        // creating the key would create the failed ancestor with defaults
        var ancestors = key.Annotation.GetAllAncestorKeys();
        var failed = ancestors.Where(ancestor => overlay.FailedAnnotations.Contains(ancestor.ToString())).ToList();

        if (failed.Count > 0)
        {
            return new ViewImportResult(
                ViewImportOutcome.Skipped,
                ErrorCode: ErrorCodes.DependencyFailed,
                Message: failed.Count == 1
                    ? $"The ancestor {failed[0]} failed in this import."
                    : $"The ancestors {string.Join(", ", failed)} failed in this import.");
        }

        return await RetryAsync(() => ImportCoreAsync(key, annotation, ancestors, options, overlay));
    }

    private async Task<ViewImportResult> ImportCoreAsync(
        FullKey fullKey,
        Annotation annotation,
        IReadOnlyList<AnnotationKey> ancestors,
        ViewImportOptions options,
        ViewImportOverlay overlay)
    {
        var store = Store(fullKey.OrganizationName);
        var node = await store.LoadAsync(fullKey, withConfiguration: false, withAudit: false);

        if (node.State == AnnotationItemState.Active)
        {
            return await ImportActiveAsync(store, node, annotation, options);
        }

        // the keys the create writes, ancestors first; an ancestor that an earlier file of this import made active is
        // not among them, also in a dry run, where it is not stored
        var states = await AnnotationItemStore.ReadStatesAsync(store.Container, ancestors.Select(ancestor => FullKey.Create(ancestor, fullKey)));
        var written = ancestors
            .Where(ancestor => states[ancestor.ToString()] != AnnotationItemState.Active
                               && !overlay.ActiveAnnotations.Contains(ancestor.ToString()))
            .Select(ancestor => (Key: ancestor, IsDeleted: states[ancestor.ToString()] == AnnotationItemState.Deleted))
            .Append((Key: fullKey.Annotation, IsDeleted: node.State == AnnotationItemState.Deleted))
            .ToList();
        var deleted = written.Where(write => write.IsDeleted).Select(write => write.Key).ToList();

        if (deleted.Count > 0 && options.Existing == ViewImportExisting.Skip)
        {
            return new ViewImportResult(
                ViewImportOutcome.Skipped,
                ErrorCode: ErrorCodes.AnnotationDeleted,
                Message: deleted.Count == 1
                    ? $"The annotation {deleted[0]} is deleted, and Skip leaves it deleted."
                    : $"The annotations {string.Join(", ", deleted)} are deleted, and Skip leaves them deleted.");
        }

        if (!options.DryRun)
        {
            try
            {
                await CreateCoreAsync(store, fullKey, annotation, options.Actor);
            }
            catch (ConflictException exception)
            {
                // created meanwhile: decided again, as an update
                throw new AnnotationConcurrencyException(exception.Message);
            }
        }

        foreach (var (key, isDeleted) in written)
        {
            overlay.AddActiveAnnotation(key, isDeleted);
        }

        var created = written.Take(written.Count - 1).Where(write => !write.IsDeleted).Select(write => write.Key).ToList();
        var warnings = new List<ViewImportWarning>();

        if (created.Count > 0)
        {
            warnings.Add(new ViewImportWarning
            {
                Code = ErrorCodes.AncestorCreated,
                Message = created.Count == 1
                    ? $"The missing ancestor {created[0]} is created with defaults."
                    : $"The missing ancestors {string.Join(", ", created)} are created with defaults.",
            });
        }

        if (deleted.Count > 0)
        {
            warnings.Add(new ViewImportWarning
            {
                Code = ErrorCodes.AnnotationRecreated,
                Message = deleted.Count == 1
                    ? $"The deleted annotation {deleted[0]} is re-created: its configuration goes to the versions and starts empty, and the children deleted with it stay deleted."
                    : $"The deleted annotations {string.Join(", ", deleted)} are re-created: their configurations go to the versions and start empty, and the children deleted with them stay deleted.",
            });
        }

        return new ViewImportResult(ViewImportOutcome.Created, Warnings: warnings.Count > 0 ? warnings : null);
    }

    // An equal document (AnnotationAuditDiff finds no change) is Unchanged; otherwise Skip keeps the stored one, and
    // Overwrite replaces its content with an Updated audit entry.
    private async Task<ViewImportResult> ImportActiveAsync(AnnotationItemStore store, AnnotationNode node, Annotation annotation, ViewImportOptions options)
    {
        var key = node.Key.Annotation;
        var changes = AnnotationAuditDiff.Compute(node.Entity!.ToModel(), AnnotationFactory.Normalize(annotation, key), _audit.MaxValueLength);

        if (changes.IsEmpty)
        {
            return new ViewImportResult(ViewImportOutcome.Unchanged);
        }

        if (options.Existing == ViewImportExisting.Skip)
        {
            return new ViewImportResult(
                ViewImportOutcome.Skipped,
                ErrorCode: ErrorCodes.DocumentExists,
                Message: $"The view has a different annotation {key}, and Skip keeps it.");
        }

        if (!options.DryRun)
        {
            await store.LoadDetailsAsync(node, withConfiguration: false, withAudit: true);
            await UpdateCoreAsync(store, node, annotation, options.Actor);
        }

        return new ViewImportResult(ViewImportOutcome.Updated);
    }
}
