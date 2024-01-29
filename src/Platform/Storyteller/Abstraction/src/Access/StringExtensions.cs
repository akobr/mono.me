namespace _42.Platform.Storyteller.Access;

public static class StringExtensions
{
    public static string ToKey(this string @this)
    {
        return @this
            .Trim()
            .ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('@', '-')
            .Replace('.', '-');
    }
}
