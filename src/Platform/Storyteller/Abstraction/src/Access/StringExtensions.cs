using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.Access;

// TODO: remove from here
public static class StringExtensions
{
    private static readonly Regex InvalidCharacterRegex = new(@"[^a-z0-9\-_]", RegexOptions.Compiled);

    public static string ToNormalizedKey(this string @this)
    {
        var lowerKey = @this.Trim().ToLowerInvariant();
        var normalizedKey = InvalidCharacterRegex.Replace(lowerKey, "_");
        return normalizedKey;
    }
}
