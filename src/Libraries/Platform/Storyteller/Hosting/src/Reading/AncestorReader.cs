using _42.Platform.Storyteller.Sdk.Configuring;

namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// The ancestors of a key, as who am I and the catalog's GetWithAncestorsAsync and ListWithAncestorsAsync read them: those
// of AnnotationKey.GetAllAncestorKeys(), in the organization, project and view of the key, each in any status but
// deleted. A 403 AnnotationRestricted makes an ancestor restricted, a 404 makes it missing (deleted after the key was
// read), and any other failure fails the read as a whole. Holds no state: a caller gives its own point read, with its
// own tokens. The descendant informer gives one that keeps a failure as an answer, for the ancestors of a target that
// requires them active, and decides about them here, by the same rule as the lists.
internal static class AncestorReader
{
    // The full keys of the ancestors of a key, roots first. The key itself is not one of them.
    public static IReadOnlyList<FullKey> KeysOf(FullKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key.Annotation.GetAllAncestorKeys().Select(ancestor => FullKey.Create(ancestor, key)).ToList();
    }

    // The same keys in the chain order, nearest first, as StorytellerExecutionContext.Chain has their snapshots: for an
    // execution the context, the usage, the subject and the responsibility.
    public static IReadOnlyList<FullKey> ChainOf(FullKey key)
    {
        var ancestors = KeysOf(key);

        return StorytellerExecutionContext.ChainOrder
            .SelectMany(type => ancestors.Where(ancestor => ancestor.Annotation.Type == type))
            .ToList();
    }

    // One ancestor. read: the point read of a key with includeInactive and without deleted ones; it throws as
    // CatalogReader.ReadAsync does.
    public static async Task<AncestorRead> ReadAsync(FullKey ancestor, Func<FullKey, Task<StorytellerAnnotation>> read)
    {
        ArgumentNullException.ThrowIfNull(ancestor);
        ArgumentNullException.ThrowIfNull(read);

        try
        {
            return new AncestorRead(ancestor, await read(ancestor).ConfigureAwait(false), null);
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsRestricted(exception) || CatalogReader.IsMissing(exception))
        {
            return new AncestorRead(ancestor, null, exception);
        }
    }

    // The ancestors of a key at once, by their full keys. skipped: the full keys of ancestors that are not read; a
    // context has them as unreadable.
    public static async Task<IReadOnlyDictionary<string, AncestorRead>> ReadAllAsync(FullKey key, IReadOnlyCollection<string> skipped, Func<FullKey, Task<StorytellerAnnotation>> read)
    {
        ArgumentNullException.ThrowIfNull(skipped);

        var reads = KeysOf(key)
            .Where(ancestor => !skipped.Contains(ancestor.ToString(), StringComparer.Ordinal))
            .Select(ancestor => ReadAsync(ancestor, read))
            .ToList();

        // Every read ends before the first failure is thrown, so none is left unobserved.
        return (await Task.WhenAll(reads).ConfigureAwait(false)).ToDictionary(result => result.Key.ToString(), StringComparer.Ordinal);
    }

    // Each distinct ancestor of the keys, read once, by full key: what a list and a poll of the descendant informer read
    // for their descendants. known: an ancestor that the caller read already, which serves instead of a second read;
    // null without one. read: the caller's read of one ancestor, with its own limit: the lists' throws as ReadAsync
    // does, the informer's keeps a failure as an answer.
    public static async Task<IReadOnlyDictionary<string, AncestorRead>> ReadDistinctAsync(IEnumerable<FullKey> keys, AncestorRead? known, Func<FullKey, Task<AncestorRead>> read)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(read);

        var knownKey = known?.Key.ToString();
        var reads = new Dictionary<string, Task<AncestorRead>>(StringComparer.Ordinal);

        foreach (var ancestor in keys.SelectMany(key => KeysOf(key)))
        {
            var text = ancestor.ToString();

            if (reads.ContainsKey(text))
            {
                continue;
            }

            reads[text] = known is not null && string.Equals(text, knownKey, StringComparison.Ordinal)
                ? Task.FromResult(known)
                : read(ancestor);
        }

        // Every read ends before the first failure is thrown, so none is left unobserved.
        var results = await Task.WhenAll(reads.Values).ConfigureAwait(false);
        return results.ToDictionary(result => result.Key.ToString(), StringComparer.Ordinal);
    }

    // Who am I for a key: its annotation, and its ancestors from their reads, by full key. An ancestor that is restricted,
    // or that has no read because it was skipped, is unreadable; a missing one is left out. reads: may hold the reads of
    // other keys too, as those of a list do.
    public static StorytellerExecutionContext ContextOf(FullKey key, StorytellerAnnotation annotation, IReadOnlyDictionary<string, AncestorRead> reads, DateTimeOffset readAt)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        ArgumentNullException.ThrowIfNull(reads);

        var ancestors = new Dictionary<AnnotationType, StorytellerAnnotation>();
        var unreadable = new List<string>();

        // In the order of the ancestors, roots first.
        foreach (var ancestor in KeysOf(key).Select(ancestor => ancestor.ToString()))
        {
            if (!reads.TryGetValue(ancestor, out var read))
            {
                unreadable.Add(ancestor);
            }
            else if (read.Annotation is { } snapshot)
            {
                ancestors[snapshot.Type] = snapshot;
            }
            else if (read.IsRestricted)
            {
                unreadable.Add(ancestor);
            }
        }

        return new StorytellerExecutionContext
        {
            Key = key.ToString(),
            Annotation = annotation,
            Ancestors = ancestors,
            Unreadable = unreadable,
            ReadAt = readAt,
        };
    }

    // Whether every ancestor of a key is active at a time. One without a snapshot is not: a missing ancestor took its
    // descendants with it. reads: as for ContextOf.
    public static bool AreActive(FullKey key, IReadOnlyDictionary<string, AncestorRead> reads, DateTimeOffset now)
    {
        return FindInactive(key, reads, now) is null;
    }

    // The ancestor that keeps a key from being under active ancestors at a time; null when every ancestor is active. One
    // whose read told nothing (IsUndecided) comes before the others, as nothing can be told about the key then.
    // Otherwise it is the first one in the chain order that is missing, deleted or not active: the one that ends the
    // key. An ancestor without a read counts as missing. reads: as for ContextOf.
    public static AncestorRead? FindInactive(FullKey key, IReadOnlyDictionary<string, AncestorRead> reads, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reads);

        AncestorRead? inactive = null;

        foreach (var ancestor in ChainOf(key))
        {
            var read = reads.GetValueOrDefault(ancestor.ToString()) ?? new AncestorRead(ancestor, null, null);

            if (read.IsUndecided)
            {
                return read;
            }

            if (inactive is null && !IsActive(read, now))
            {
                inactive = read;
            }
        }

        return inactive;
    }

    private static bool IsActive(AncestorRead read, DateTimeOffset now)
    {
        return read.Annotation is { DeletedAt: null } annotation && annotation.GetStatus(now) == AnnotationStatus.Active;
    }
}
