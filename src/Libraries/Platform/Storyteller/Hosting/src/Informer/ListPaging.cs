using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;

namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// Every page of one list of annotations, for the informers and the catalog's lists: it follows the continuation tokens
// until the platform answers none, through empty pages (Cosmos DB may cut a page short), and keeps the item listed last
// per annotation key, as pages have no order. A token answered twice, or more than MaxPages pages, is a platform that
// answers its first page again, and fails the list.
internal static class ListPaging
{
    // A list that pages on past this is a platform that answers its first page again.
    internal const int MaxPages = 1000;

    // readPage: the page at a token, null for the first; it throws as a failed page does. key: the Key of the exception.
    // list: what the messages name, "the executions of {key}".
    public static async Task<IReadOnlyDictionary<string, Annotation>> ReadAllAsync(Func<string?, Task<AnnotationPage>> readPage, string key, string list)
    {
        ArgumentNullException.ThrowIfNull(readPage);

        var items = new Dictionary<string, Annotation>(StringComparer.Ordinal);
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        string? token = null;

        for (var pages = 1; ; pages++)
        {
            var page = await readPage(token).ConfigureAwait(false);

            foreach (var item in page.Items)
            {
                if (!string.IsNullOrEmpty(item.AnnotationKey))
                {
                    items[item.AnnotationKey] = item;
                }
            }

            if (page.ContinuationToken is not { } next)
            {
                return items;
            }

            if (!tokens.Add(next))
            {
                throw Unending(key, list, "the platform answered a continuation token it had answered before");
            }

            if (pages >= MaxPages)
            {
                throw Unending(key, list, $"it had more than {MaxPages} pages");
            }

            token = next;
        }
    }

    private static StorytellerConfigurationException Unending(string key, string list, string reason)
    {
        return new StorytellerConfigurationException(
            $"Listing {list} did not end: {reason}. A Storyteller API without the fix that pages annotation lists through their continuation tokens answers the first page again; update the API.",
            key,
            null,
            null,
            false);
    }
}
