namespace _42.Platform.Storyteller.Mcp.Tools;

// Reads one host page of an API list. The API filters some pages in memory (inactive annotations) and readPage can
// filter them further (the names of find_annotations), so a page can be empty and still have a continuation token; a
// few such pages are skipped here, so the model does not have to follow empty results. A filter belongs in readPage,
// before the offset of the host cursor is taken, so that a re-read of the same API page lands on the same item.
public static class ApiPageReader
{
    public const int MaxEmptyPages = 5;

    public static async Task<HostPage.Page<T>> ReadAsync<T>(
        string? continuationToken,
        int? limit,
        Func<string?, Task<(IReadOnlyList<T> Items, string? Next)>> readPage)
    {
        var cursor = HostPage.Decode(continuationToken);
        var size = HostPage.ClampLimit(limit);

        for (var read = 1; ; read++)
        {
            var (items, next) = await readPage(cursor.ApiToken);
            var page = HostPage.Take(items, next, cursor, size);

            if (page.Count > 0 || page.ContinuationToken is null || read >= MaxEmptyPages)
            {
                return page;
            }

            cursor = HostPage.Decode(page.ContinuationToken);
        }
    }
}
