using System.Text.Json.Nodes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding.Object;

internal static class JsonTokenConverter
{
    public static JsonNode? ToNode(JToken token)
    {
        return JsonNode.Parse(token.ToString(Formatting.None));
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
