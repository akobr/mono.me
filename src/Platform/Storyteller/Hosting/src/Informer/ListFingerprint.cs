using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.Informer;

// Tells between two polls whether an annotation changed, from its list item alone: its Modified stamp (instant, actor
// kind and id), which every write that changes something moves, or, for an item without one, a hash of the item's
// members. List values are lossy, but the same stored values list the same, so the hash is stable between polls; the
// snapshots never come from list items.
internal static class ListFingerprint
{
    // Without the application's JsonConvert.DefaultSettings.
    private static readonly JsonSerializer Serializer = JsonSerializer.Create();

    public static string Of(Annotation item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Modified is { } modified)
        {
            return string.Create(CultureInfo.InvariantCulture, $"modified:{modified.At.UtcTicks}:{modified.By?.Kind}:{modified.By?.Id}");
        }

        var canonical = Canonical(JToken.FromObject(item, Serializer)).ToString(Formatting.None);
        return $"hash:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))}";
    }

    // Members by name, so an order the server does not promise cannot change the hash.
    private static JToken Canonical(JToken token)
    {
        return token switch
        {
            JObject members => new JObject(members.Properties()
                .OrderBy(member => member.Name, StringComparer.Ordinal)
                .Select(member => new JProperty(member.Name, Canonical(member.Value)))),
            JArray items => new JArray(items.Select(Canonical)),
            _ => token,
        };
    }
}
