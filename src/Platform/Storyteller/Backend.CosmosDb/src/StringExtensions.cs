using System;
using System.Text;

namespace _42.Platform.Storyteller;

public static class StringExtensions
{
    public static string ToBase64(this string @this)
    {
        var bytes = Encoding.UTF8.GetBytes(@this);
        return Convert.ToBase64String(bytes);
    }

    public static bool TryFromBase64(this string @this, out string decodedText)
    {
        var bytes = new Span<byte>(new byte[@this.Length]);
        var result = Convert.TryFromBase64String(@this, bytes, out var bytesWritten);
        decodedText = Encoding.UTF8.GetString(bytes.ToArray(), 0, bytesWritten);
        return result;
    }

    public static string FromBase64(this string @this)
    {
        var bytes = Convert.FromBase64String(@this);
        return Encoding.UTF8.GetString(bytes);
    }
}
