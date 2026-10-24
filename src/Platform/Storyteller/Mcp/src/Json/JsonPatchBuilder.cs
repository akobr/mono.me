using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Json;

// The JSON Patch (RFC 6902) that makes a stored document equal a desired one, by top-level property: a changed or
// removed property is tested against its stored value, then replaced or removed; a new property is added. A concurrent
// change of a property the patch touches fails its test (412 PatchTestFailed) instead of being overwritten.
public static class JsonPatchBuilder
{
    public static JArray Create(JObject stored, JObject desired)
    {
        var operations = new JArray();

        foreach (var property in stored.Properties())
        {
            if (desired.Property(property.Name, StringComparison.Ordinal) is null)
            {
                AddTest(operations, Pointer(property.Name), property.Value);
                operations.Add(new JObject { ["op"] = "remove", ["path"] = Pointer(property.Name) });
            }
        }

        foreach (var property in desired.Properties())
        {
            var current = stored.Property(property.Name, StringComparison.Ordinal);

            if (current is null)
            {
                operations.Add(ValueOperation("add", Pointer(property.Name), property.Value));
            }
            else if (!AreEqual(current.Value, property.Value))
            {
                AddTest(operations, Pointer(property.Name), current.Value);
                operations.Add(ValueOperation("replace", Pointer(property.Name), property.Value));
            }
        }

        // The API's JSON Patch takes the pointer escapes ~0 and ~1 literally, so a remove of "/a~1b" would silently do
        // nothing. A change of a property whose name needs them replaces the whole document instead.
        if (operations.Any(operation => operation.Value<string>("path")!.Contains('~', StringComparison.Ordinal)))
        {
            operations = [];
            AddTest(operations, string.Empty, stored);
            operations.Add(ValueOperation("replace", string.Empty, desired));
        }

        return operations;
    }

    // A client that parses dates (the SDK's template client does) holds a date-like string as a DateTime: the text
    // the server stores is unknown, so it is compared by parsing the desired value the same way and never tested.
    private static bool AreEqual(JToken stored, JToken desired)
    {
        return JToken.DeepEquals(stored, desired)
            || (HasDate(stored) && JToken.DeepEquals(stored, WithDates(desired)));
    }

    private static void AddTest(JArray operations, string path, JToken stored)
    {
        if (!HasDate(stored))
        {
            operations.Add(ValueOperation("test", path, stored));
        }
    }

    private static bool HasDate(JToken token)
    {
        return token.Type == JTokenType.Date
            || (token is JContainer container && container.Descendants().Any(item => item.Type == JTokenType.Date));
    }

    private static JToken WithDates(JToken token)
    {
        using var reader = new JsonTextReader(new StringReader(token.ToString(Formatting.None))) { DateParseHandling = DateParseHandling.DateTime };
        return JToken.ReadFrom(reader);
    }

    private static JObject ValueOperation(string operation, string path, JToken value)
    {
        return new JObject
        {
            ["op"] = operation,
            ["path"] = path,
            ["value"] = value.DeepClone(),
        };
    }

    // RFC 6901: "~" is "~0" and "/" is "~1"; an empty name is the pointer "/".
    private static string Pointer(string name)
    {
        return "/" + name.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    }
}
