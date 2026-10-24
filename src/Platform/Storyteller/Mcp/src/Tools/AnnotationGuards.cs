using System.Globalization;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The checks a write makes before it calls the API, because the API does what was asked without asking back: a create
// re-creates deleted keys and ancestors silently, and a configuration write on a missing annotation stores a document
// that is inherited but never listed.
public static class AnnotationGuards
{
    // FindDeletedAnnotationsAsync takes at most this many keys per call.
    public const int MaxKeysPerLookup = 100;

    // A new name follows the sform rule; existing names are addressed as they are. Every name of a key is the name of a
    // responsibility, a subject, a context or a unit (rst.R, sbt.S, cnt.S.C, unt.R.U among the key and its ancestors),
    // and it is new only when that annotation does not exist yet, deleted ones included. Only a name outside the rule
    // is looked up, so a write with ordinary names makes no extra call.
    public static async Task EnsureNewNamesAsync(
        Sdk.IAnnotationsApiClient annotations,
        string organization,
        string project,
        string view,
        IEnumerable<AnnotationKey> keys,
        CancellationToken cancellationToken)
    {
        var naming = keys
            .SelectMany(key => key.GetAllAncestorKeys().Append(key))
            .Where(key => key.Type is AnnotationType.Responsibility or AnnotationType.Subject or AnnotationType.Context or AnnotationType.Unit)
            .DistinctBy(key => key.ToString(), StringComparer.Ordinal)
            .Where(key => !AnnotationNames.IsValid(key.Name));

        foreach (var key in naming)
        {
            if (!await ExistsAsync(annotations, organization, project, view, key.ToString(), cancellationToken))
            {
                throw new ToolInputException(
                    $"The name '{key.Name}' of the new key '{key}' is not allowed: a new name has 1 to {AnnotationNames.MaxLength} "
                    + "letters, numbers, dashes or underscores.");
            }
        }
    }

    // The key and every ancestor, roots first, without duplicates across the keys.
    public static IReadOnlyList<string> WithAncestors(IEnumerable<AnnotationKey> keys)
    {
        return keys
            .SelectMany(key => key.GetAllAncestorKeys().Append(key))
            .Select(key => key.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static async Task<IReadOnlyList<Sdk.Annotation>> FindDeletedAsync(
        Sdk.IAnnotationsApiClient annotations,
        string organization,
        string project,
        string view,
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken)
    {
        var deleted = new List<Sdk.Annotation>();

        foreach (var chunk in keys.Chunk(MaxKeysPerLookup))
        {
            var found = await annotations.FindDeletedAnnotationsAsync(organization, project, view, new Sdk.AnnotationKeysRequest { Keys = chunk.ToList() }, cancellationToken);
            deleted.AddRange(found ?? []);
        }

        return deleted;
    }

    public static void EnsureNoneDeleted(IReadOnlyList<Sdk.Annotation> deleted)
    {
        if (deleted.Count == 0)
        {
            return;
        }

        var list = string.Join("; ", deleted.Select(Describe));
        throw new ToolInputException(
            $"Deleted annotations are in the way: {list}. A create would re-create them empty (their configurations go to "
            + "the versions) and leave their other deleted descendants deleted. Restore them with restore_annotations and "
            + "call again, or pass recreateDeleted: true to re-create them.");
    }

    // A configuration write needs its annotation: without one the document is inherited but not listed, and a deleted
    // one is refused by the API anyway.
    public static async Task EnsureAnnotationExistsAsync(
        Sdk.IAnnotationsApiClient annotations,
        string organization,
        string project,
        string view,
        string key,
        CancellationToken cancellationToken)
    {
        Sdk.Annotation annotation;

        try
        {
            annotation = await annotations.GetAnnotationAsync(organization, project, view, key, true, true, cancellationToken);
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            throw new ToolInputException(
                $"The annotation '{key}' does not exist in the view '{view}'. Create it first with set_annotation (or "
                + "create_annotations): a configuration without its annotation is inherited but not listed.");
        }

        if (annotation.Deletion is not null)
        {
            throw new ToolInputException(
                $"The annotation '{key}' is deleted ({Describe(annotation)}). Restore it with restore_annotations before "
                + "changing its configuration.");
        }
    }

    private static async Task<bool> ExistsAsync(
        Sdk.IAnnotationsApiClient annotations,
        string organization,
        string project,
        string view,
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            await annotations.GetAnnotationAsync(organization, project, view, key, true, true, cancellationToken);
            return true;
        }
        catch (Sdk.ApiException exception) when (exception.StatusCode == 404)
        {
            return false;
        }
    }

    private static string Describe(Sdk.Annotation annotation)
    {
        var deletion = annotation.Deletion;

        if (deletion is null)
        {
            return annotation.AnnotationKey;
        }

        var by = deletion.DeletedBy?.Name ?? deletion.DeletedBy?.Id;
        return $"{annotation.AnnotationKey} (deleted {deletion.DeletedAt.ToString("u", CultureInfo.InvariantCulture)}"
            + (string.IsNullOrEmpty(by) ? string.Empty : $" by {by}")
            + (string.IsNullOrEmpty(deletion.RootKey) || deletion.RootKey == annotation.AnnotationKey ? string.Empty : $" with {deletion.RootKey}")
            + ")";
    }
}
