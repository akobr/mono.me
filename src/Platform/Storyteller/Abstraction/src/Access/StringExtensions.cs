using System.Text;

namespace _42.Platform.Storyteller.Access;

public static class StringExtensions
{
    public static string ToNormalizedKey(this string @this)
    {
        var lowerKey = @this.Trim().ToLowerInvariant();
        var keyTemp = new StringBuilder(lowerKey);

        for (var i = 0; i < keyTemp.Length; i++)
        {
            if (!char.IsLetterOrDigit(keyTemp[i])
                && keyTemp[i] != '-'
                && keyTemp[i] != '_')
            {
                keyTemp[i] = '_';
            }
        }

        var normalizedKey = keyTemp.ToString();
        return normalizedKey;
    }
}
