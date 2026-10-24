using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// The merge of the API (Backend.CosmosDb JsonExtensions.MergeInto): objects merge property by property, arrays grow by
// union, and an incoming null never erases a value.
public static class ConfigurationMerge
{
    private static readonly JsonMergeSettings Settings = new()
    {
        MergeArrayHandling = MergeArrayHandling.Union,
        MergeNullValueHandling = MergeNullValueHandling.Ignore,
    };

    public static void Merge(JObject target, JObject incoming)
    {
        target.Merge(incoming, Settings);
    }

    // A new document: the stored one (or nothing) with the incoming one merged in, as a write would store it.
    public static JObject Merged(JObject? stored, JObject incoming)
    {
        var result = stored?.DeepClone() as JObject ?? new JObject();
        Merge(result, incoming);
        return result;
    }
}
