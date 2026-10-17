namespace _42.Platform.Storyteller.Transferring;

// The include filter of an export or an import: the kinds of documents it covers.
public static class ViewArchiveInclude
{
    // Every kind, in import order.
    public static IReadOnlyList<ViewArchiveContent> All { get; } = Enum.GetValues<ViewArchiveContent>();

    // The kinds of a filter, distinct and in import order; null or empty means all four.
    public static IReadOnlyList<ViewArchiveContent> Resolve(IEnumerable<ViewArchiveContent>? include)
    {
        var resolved = include?.Distinct().Order().ToList();
        return resolved is { Count: > 0 } ? resolved : All;
    }
}
