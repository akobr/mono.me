namespace _42.Platform.Storyteller.AspireHosting;

// The rules of FullKey.TryParse (Abstractions.Annotations), copied so an AppHost does not reference it: '@', a non-empty
// organization, project and view, and an annotation key of a known type code with the segment count of that type, so
// five to eight segments. A test compares the two over a table of keys.
internal static class StorytellerKeyRule
{
    private const char FullKeyStart = '@';
    private const char Separator = '.';

    // The segments of an annotation key per type code, the type code included; codes are case-insensitive, as in
    // AnnotationTypeCodes.ValidCodes.
    private static readonly Dictionary<string, int> SegmentCounts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rst"] = 2,
        ["sbt"] = 2,
        ["usg"] = 3,
        ["cnt"] = 3,
        ["unt"] = 3,
        ["exe"] = 4,
        ["uxe"] = 5,
    };

    public static bool IsFull(string key)
    {
        return key.StartsWith(FullKeyStart);
    }

    public static bool IsFullKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsFull(text))
        {
            return false;
        }

        var segments = text[1..].Split(Separator);

        return !segments.Any(string.IsNullOrWhiteSpace)
            && segments.Length is >= 5 and <= 8
            && SegmentCounts.TryGetValue(segments[3], out var count)
            && segments.Length - 3 == count;
    }

    // An annotation key, checked as the full key it becomes. WithView takes one segment per name, so any view gives the
    // same answer as the placeholder; the callback prefixes the real one.
    public static bool IsAnnotationKey(string? text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && !IsFull(text)
            && IsFullKey($"{FullKeyStart}organization.project.view.{text}");
    }

    // One segment of a key: an organization, project, view, subject, responsibility or context name.
    public static bool IsName(string? text)
    {
        return !string.IsNullOrWhiteSpace(text) && !text.Contains(Separator);
    }
}
