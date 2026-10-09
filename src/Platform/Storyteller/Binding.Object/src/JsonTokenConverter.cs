using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object;

internal static class JsonTokenConverter
{
    /// <summary>
    /// Converts a definition or context token. Stored documents are read with a depth of 64, so
    /// <paramref name="maxDepth"/> only guards tokens built in memory; past it the conversion stops with a depth limit
    /// instead of exhausting the stack.
    /// </summary>
    public static JsonNode? ToNode(JToken token, int maxDepth)
    {
        return ToNode(token, maxDepth, depth: 0);
    }

    public static JToken ToToken(JsonNode? node)
    {
        if (node is null)
        {
            return JValue.CreateNull();
        }

        using var reader = new JsonTextReader(new StringReader(node.ToJsonString()))
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Decimal,
        };

        return JToken.Load(reader);
    }

    private static JsonNode? ToNode(JToken token, int maxDepth, int depth)
    {
        switch (token.Type)
        {
            case JTokenType.Null:
            case JTokenType.Undefined:
                return null;
            case JTokenType.String:
                // JsonNode.Parse would back the value with a JsonElement and copy the text on every read.
                return JsonValue.Create(token.Value<string>());
            case JTokenType.Boolean:
                return JsonValue.Create(token.Value<bool>());
            case JTokenType.Object:
                EnsureDepth(maxDepth, depth);
                var obj = new JsonObject();
                foreach (var property in ((JObject)token).Properties())
                {
                    obj.Add(property.Name, ToNode(property.Value, maxDepth, depth + 1));
                }

                return obj;
            case JTokenType.Array:
                EnsureDepth(maxDepth, depth);
                var array = new JsonArray();
                foreach (var item in (JArray)token)
                {
                    array.Add(ToNode(item, maxDepth, depth + 1));
                }

                return array;
            default:
                // Numbers and other scalars keep the JSON text so the decimal round-trip matches Parse.
                return JsonNode.Parse(token.ToString(Formatting.None));
        }
    }

    private static void EnsureDepth(int maxDepth, int depth)
    {
        if (depth >= maxDepth)
        {
            throw new EvaluationLimitExceededException(
                EvaluationLimitKind.Depth,
                maxDepth,
                $"Object binding input nests deeper than {maxDepth} levels.");
        }
    }
}
