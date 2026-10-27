using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Json;

public class JObjectConverter : JsonConverter<JObject>
{
    // The levels of a JObject the converter writes. Storage reads 64 levels, and a binding result of up to 64 levels
    // may sit deep inside a resolved document, so 128 covers every document the API holds or resolves.
    public const int MaxDepth = 128;

    public override JObject? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonNode = JsonNode.Parse(ref reader);

        if (jsonNode is null)
        {
            return null;
        }

        return JObject.Parse(jsonNode.ToJsonString());
    }

    // The object is written by a writer of its own and spliced in as one value, so the depth limit of the enclosing
    // writer (32 under the MVC options) counts the object as one level: a configuration whose content an API read can
    // format (?format=) can also be written in its envelope. The writer of the object takes the escaping of the
    // enclosing one, so a compact writer writes the same bytes as when the object is written straight into it; an
    // indented one indents the object from its own first column.
    public override void Write(Utf8JsonWriter writer, JObject value, JsonSerializerOptions options)
    {
        var jsonString = value.ToString(Newtonsoft.Json.Formatting.None);
        using var jsonDocument = JsonDocument.Parse(jsonString, new JsonDocumentOptions { MaxDepth = MaxDepth });
        var buffer = new ArrayBufferWriter<byte>();

        using (var objectWriter = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Encoder = writer.Options.Encoder,
                   Indented = writer.Options.Indented,
                   IndentCharacter = writer.Options.IndentCharacter,
                   IndentSize = writer.Options.IndentSize,
                   NewLine = writer.Options.NewLine,
                   MaxDepth = MaxDepth,
               }))
        {
            jsonDocument.WriteTo(objectWriter);
        }

        writer.WriteRawValue(buffer.WrittenSpan, skipInputValidation: true);
    }
}
