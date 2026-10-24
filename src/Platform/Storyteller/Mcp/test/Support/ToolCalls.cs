using System.Text.Json;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

internal static class ToolCalls
{
    // Arguments as JSON elements, which the client serializes without reflection over anonymous types.
    public static IReadOnlyDictionary<string, object?> Arguments(object? arguments)
    {
        if (arguments is null)
        {
            return new Dictionary<string, object?>();
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
    }

    public static async Task<CallToolResult> CallAsync(this McpClient client, string tool, object? arguments = null)
    {
        return await client.CallToolAsync(tool, Arguments(arguments));
    }

    public static string Text(this CallToolResult result)
    {
        return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
    }
}
