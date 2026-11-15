using _42.Platform.Storyteller.Sdk.Configuring;

namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// The ancestors of a key, as who am I and the catalog's GetWithAncestorsAsync and ListWithAncestorsAsync read them: those
// of AnnotationKey.GetAllAncestorKeys(), in the organization, project and view of the key, each in any status but
// deleted. A 403 AnnotationRestricted makes an ancestor restricted, a 404 makes it missing (deleted after the key was
// read), and any other failure fails the read as a whole. Holds no state: a caller gives its own point read, with its
// own tokens.
internal static class AncestorReader
{
    // The full keys of the ancestors of a key, roots first. The key itself is not one of them.
    public static IReadOnlyList<FullKey> KeysOf(FullKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return key.Annotation.GetAllAncestorKeys().Select(ancestor => FullKey.Create(ancestor, key)).ToList();
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
        ArgumentNullException.ThrowIfNull(reads);

        return KeysOf(key).All(ancestor => reads.GetValueOrDefault(ancestor.ToString())?.Annotation?.GetStatus(now) == AnnotationStatus.Active);
    }
}
