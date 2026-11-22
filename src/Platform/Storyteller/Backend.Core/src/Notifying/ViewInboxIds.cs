using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller.Notifying;

// The id of an inbox item, {yyyyMMddHHmmssfffffff}{32 hex}: its order is the time order, so the list pages on it, and
// it is a clean path segment.
public static partial class ViewInboxIds
{
    public const int Length = 53;

    private const string TimeFormat = "yyyyMMddHHmmssfffffff";
    private const int SuffixLength = 32;

    public static string NewId(DateTimeOffset time)
    {
        return FormatTime(time) + Guid.NewGuid().ToString("N");
    }

    // The id of a push with a key: the same key and time give the same id.
    public static string ForKey(DateTimeOffset time, string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        // lower case, as IsValid and the GUID of NewId
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return FormatTime(time) + hash[..SuffixLength];
    }

    public static bool IsValid(string? id)
    {
        return id is not null && IdPattern().IsMatch(id);
    }

    // In UTC and with the invariant culture: one instant has one text, whatever its offset and the thread's calendar.
    private static string FormatTime(DateTimeOffset time)
    {
        return time.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"^[0-9]{21}[0-9a-f]{32}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
}
