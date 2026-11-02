using System.Globalization;

namespace _42.Platform.UI.Admin.AutomationTests.Data;

// The generators of the Annotations section: a set of keys that share a prefix, which one key: filter lists together,
// and the instants of an annotation's validity dates. A single key part (AnnotationPart) is in PlatformData.cs.
public sealed partial class PlatformData
{
    // A set's prefix leaves room for "-<n>" within a part's 40 characters.
    private const int MaxKeySetPrefixLength = MaxPartLength - 6;

    // The common start of a key set, "<code>.<p>" with a new generated part <p>, cut so that "-<n>" still fits in the
    // part's 40 characters: the filter key:<code>.<p> lists the set (AnnotationKeySet) and nothing else. Only for a type
    // with a single part (rst or sbt), so the keys have no ancestors to create.
    public string AnnotationKeySetPrefix(string code)
    {
        if (PartsOf(code, nameof(code)).Length != 1)
        {
            throw new ArgumentException($"A key set takes a type with one part (rst or sbt); '{code}' has more.", nameof(code));
        }

        return $"{code}.{Cut(Data.Slug($"{RunSlug}-{Data.Hex(HexLength)}"), MaxKeySetPrefixLength)}";
    }

    // count keys "<prefix>-1" to "<prefix>-<count>" of a prefix from AnnotationKeySetPrefix, e.g. sbt.<p>-1, sbt.<p>-2
    // and sbt.<p>-3; they sort by their number up to 9.
    public static IReadOnlyList<string> AnnotationKeySet(string prefix, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);

        return [.. Enumerable.Range(1, count).Select(index => $"{prefix}-{index}")];
    }

    // An instant days from now (negative: ago) as ISO 8601 text in UTC to the minute, e.g. "2026-11-12T09:41:00Z": a
    // ValidFrom or ExpiresAt of an annotation's JSON document. The form enters dates to the minute too.
    public static string AnnotationInstant(double days)
    {
        var instant = DateTimeOffset.UtcNow.AddDays(days);
        return instant.ToString("yyyy'-'MM'-'dd'T'HH':'mm':00Z'", CultureInfo.InvariantCulture);
    }
}
