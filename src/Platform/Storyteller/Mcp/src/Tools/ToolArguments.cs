using System.Text.Json;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Tool arguments arrive as System.Text.Json; SDK bodies are Newtonsoft. A JsonElement handed to the SDK would be
// written as {"ValueKind":1}, so documents go through their raw text.
public static class ToolArguments
{
    public static JToken ToToken(JsonElement element)
    {
        using var reader = new JsonTextReader(new StringReader(element.GetRawText())) { DateParseHandling = DateParseHandling.None };
        return JToken.ReadFrom(reader);
    }

    public static JObject ToObject(JsonElement element, string name)
    {
        return ToToken(element) as JObject
            ?? throw new ToolInputException($"'{name}' must be a JSON object.");
    }

    public static JArray ToArray(JsonElement element, string name)
    {
        return ToToken(element) as JArray
            ?? throw new ToolInputException($"'{name}' must be a JSON array.");
    }

    public static string Required(string? value, string name)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new ToolInputException($"'{name}' is required.")
            : value.Trim();
    }
}
