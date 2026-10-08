using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object;

internal static class JsonTokenConverter
{
    public static JsonNode? ToNode(JToken token)
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
                var obj = new JsonObject();
                foreach (var property in ((JObject)token).Properties())
                {
                    obj.Add(property.Name, ToNode(property.Value));
                }

                return obj;
            case JTokenType.Array:
                var array = new JsonArray();
                foreach (var item in (JArray)token)
                {
                    array.Add(ToNode(item));
                }

                return array;
            default:
                // Numbers and other scalars keep the JSON text so the decimal round-trip matches Parse.
                return JsonNode.Parse(token.ToString(Formatting.None));
        }
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
}
