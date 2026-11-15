using _42.Platform.Storyteller.Sdk.Hosting.Reading;

namespace _42.Platform.Storyteller.Sdk.Hosting.Catalog;

// The default implementations of IStorytellerCatalog's GetWithAncestorsAsync and ListWithAncestorsAsync, over its
// GetAsync and ListAsync: what a test double of an application gets without a change. They know no restrictions, as
// GetAsync throws on a 403, so nothing lands in Unreadable; an ancestor that GetAsync answers null for is left out. The
// ancestors are asked for by their full keys, in the organization, project and view of the snapshot they belong to, one
// at a time, so a test double need not answer calls at once. There is no TimeProvider here: the times are the system
// clock's.
internal static class CatalogDefaults
{
    public static async Task<StorytellerExecutionContext?> GetWithAncestorsAsync(IStorytellerCatalog catalog, string key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var readAt = DateTimeOffset.UtcNow;

        if (await catalog.GetAsync(key, cancellationToken).ConfigureAwait(false) is not { } annotation)
        {
            return null;
        }

        var fullKey = KeyOf(annotation);
        var reads = new Dictionary<string, AncestorRead>(StringComparer.Ordinal);
        await ReadAncestorsAsync(catalog, fullKey, reads, cancellationToken).ConfigureAwait(false);

        return AncestorReader.ContextOf(fullKey, annotation, reads, readAt);
    }

    // ListAsync, then each distinct ancestor of its descendants once. A target that requires active ancestors is
    // narrowed here, as ListAsync of a test double may not know the requirement.
    public static async Task<IReadOnlyList<StorytellerExecutionContext>> ListWithAncestorsAsync<TAnnotation>(IStorytellerCatalog catalog, StorytellerTarget<TAnnotation> target, CancellationToken cancellationToken)
        where TAnnotation : StorytellerAnnotation
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(target);

        var readAt = DateTimeOffset.UtcNow;
        var descendants = await catalog.ListAsync(target, cancellationToken).ConfigureAwait(false);
        var now = DateTimeOffset.UtcNow;
        var reads = new Dictionary<string, AncestorRead>(StringComparer.Ordinal);
        var contexts = new List<StorytellerExecutionContext>();

        foreach (var descendant in descendants.OrderBy(descendant => descendant.Key, StringComparer.Ordinal))
        {
            var fullKey = KeyOf(descendant);
            await ReadAncestorsAsync(catalog, fullKey, reads, cancellationToken).ConfigureAwait(false);

            if (!target.RequireActiveAncestors || AncestorReader.AreActive(fullKey, reads, now))
            {
                contexts.Add(AncestorReader.ContextOf(fullKey, descendant, reads, readAt));
            }
        }

        return contexts.AsReadOnly();
    }

    // The ancestors of the key that reads does not hold yet, each with GetAsync.
    private static async Task ReadAncestorsAsync(IStorytellerCatalog catalog, FullKey key, Dictionary<string, AncestorRead> reads, CancellationToken cancellationToken)
    {
        foreach (var ancestor in AncestorReader.KeysOf(key))
        {
            var text = ancestor.ToString();

            if (!reads.ContainsKey(text))
            {
                reads[text] = new AncestorRead(ancestor, await catalog.GetAsync(text, cancellationToken).ConfigureAwait(false), null);
            }
        }
    }

    // The full key of a snapshot that the catalog answered, which names its ancestors, so a key given as an annotation
    // key works too.
    private static FullKey KeyOf(StorytellerAnnotation annotation)
    {
        return FullKey.TryParse(annotation.Key, out var key)
            ? CatalogKeys.Normalize(key)
            : throw new InvalidOperationException(
                $"The catalog answered a snapshot whose Key, '{annotation.Key}', is not a full key, @{{organization}}.{{project}}.{{view}}.{{annotation key}}, so the keys of its ancestors are not known. Give the snapshots of a test double their full keys.");
    }
}
