using System.Text.Json;

using ModelContextProtocol.Protocol;

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Tool results: the JSON of an SDK value as one text block and the same JSON as structured content. The SDK is
// Newtonsoft-based, so its values are serialized with Newtonsoft and never with System.Text.Json.
public static class ToolResults
{
    private static readonly Newtonsoft.Json.JsonSerializer Serializer = Newtonsoft.Json.JsonSerializer.Create(new JsonSerializerSettings
    {
        NullValueHandling = NullValueHandling.Ignore,
        DateParseHandling = DateParseHandling.None,
        Converters = { new StringEnumConverter() },
    });

    public static CallToolResult Json(object? value, string? notice = null)
    {
        return Create(ToObject(value), notice);
    }

    // The text is the message as it is, for the model; the structured content adds the details (status, error code).
    public static CallToolResult Error(string message, object? details = null)
    {
        var content = details is null ? new JObject() : ToObject(details);
        content["Message"] = message;

        using var document = JsonDocument.Parse(content.ToString(Formatting.None));

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = message }],
            StructuredContent = document.RootElement.Clone(),
            IsError = true,
        };
    }

    public static JToken ToToken(object? value)
    {
        return value switch
        {
            null => JValue.CreateNull(),
            JToken token => token,
            _ => JToken.FromObject(value, Serializer),
        };
    }

    // Structured content is an object: arrays and scalars are wrapped.
    private static JObject ToObject(object? value)
    {
        var token = ToToken(value);

        return token switch
        {
            JObject obj => obj,
            JArray array => new JObject { ["Items"] = array },
            _ => new JObject { ["Value"] = token },
        };
    }

    private static CallToolResult Create(JObject content, string? notice)
    {
        var json = content.ToString(Formatting.None);
        using var document = JsonDocument.Parse(json);

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = notice is null ? json : $"{notice}\n{json}" }],
            StructuredContent = document.RootElement.Clone(),
        };
    }
}
