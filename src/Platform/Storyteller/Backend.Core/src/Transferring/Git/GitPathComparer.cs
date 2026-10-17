namespace _42.Platform.Storyteller.Transferring.Git;

// Orders tree and index entries like git: by the UTF-8 bytes of the name, which is the order of code points. The
// index lists full paths in this order; a tree compares a subtree as its name followed by '/' (GetTreeEntryName),
// so both orders agree.
public sealed class GitPathComparer : IComparer<string>
{
    public static readonly GitPathComparer Instance = new();

    // The name a tree orders an entry by: "a.json" < "a/" (the subtree a) < "a0".
    public static string GetTreeEntryName(string name, bool isTree)
    {
        return isTree ? $"{name}/" : name;
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var left = x.EnumerateRunes();
        var right = y.EnumerateRunes();

        while (true)
        {
            var hasLeft = left.MoveNext();
            var hasRight = right.MoveNext();

            if (!hasLeft || !hasRight)
            {
                return hasLeft ? 1 : hasRight ? -1 : 0;
            }

            var difference = left.Current.Value.CompareTo(right.Current.Value);

            if (difference != 0)
            {
                return difference;
            }
        }
    }
}
