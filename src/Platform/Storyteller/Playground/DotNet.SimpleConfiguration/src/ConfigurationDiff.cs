namespace _42.Platform.Storyteller.Playground.DotNet.SimpleConfiguration;

// What changed between two reads of DocumentKeys: the keys of the new read in their order, then the ones that went.
public static class ConfigurationDiff
{
    public static IReadOnlyList<KeyChange> Compare(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        var changes = new List<KeyChange>();

        foreach (var (key, value) in after)
        {
            if (!before.TryGetValue(key, out var previous))
            {
                changes.Add(new KeyChange(KeyChangeKind.Added, key, null, value));
            }
            else if (!string.Equals(previous, value, StringComparison.Ordinal))
            {
                changes.Add(new KeyChange(KeyChangeKind.Changed, key, previous, value));
            }
        }

        changes.AddRange(before
            .Where(pair => !after.ContainsKey(pair.Key))
            .Select(pair => new KeyChange(KeyChangeKind.Removed, pair.Key, pair.Value, null)));

        return changes;
    }
}
