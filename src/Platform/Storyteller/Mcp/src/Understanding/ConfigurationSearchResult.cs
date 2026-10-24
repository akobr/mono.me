namespace _42.Platform.Storyteller.Mcp.Understanding;

// TruncatedBy names the budget that stopped the search: Pages, DocumentReads or MaxResults.
public sealed record ConfigurationSearchResult(
    IReadOnlyList<SearchMatch> Items,
    bool IsTruncated,
    string? TruncatedBy,
    string? Message,
    int PagesRead,
    int DocumentsRead)
{
    public int Count => Items.Count;
}
