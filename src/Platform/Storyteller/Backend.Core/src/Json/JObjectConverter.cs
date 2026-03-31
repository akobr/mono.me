using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Json;

public class JObjectConverter : JsonConverter<JObject>
{
    public override JObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);

        if (jsonNode is null)
        {
            return null;
        }

        return JObject.Parse(jsonNode.ToJsonString());
    }

    public override void Write(Utf8JsonWriter writer, JObject value, JsonSerializerOptions options)
    {
        var jsonString = value.ToString(Newtonsoft.Json.Formatting.None);
        using var jsonDocument = JsonDocument.Parse(jsonString);
        jsonDocument.WriteTo(writer);
    }
}
