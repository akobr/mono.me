using ModelContextProtocol.Server;

namespace _42.Platform.Storyteller.Mcp.Tools;

// The tools that may run while the API lets the person's client app only read (StorytellerToken.ReadOnly): those whose
// ReadOnlyHint is true, and the secret reads (SecretToolNames), which are ReadOnly = false on purpose so that clients
// confirm them, and stay governed by NoSecretReveal alone. CatalogTests pins an explicit hint on every tool.
public static class ReadOnlyToolSet
{
    public static bool Contains(string? name, bool? readOnlyHint)
    {
        return readOnlyHint == true || SecretToolNames.Contains(name);
    }

    // The tool a call matched; false when none matched, so an unknown call is refused under ReadOnly.
    public static bool Contains(IMcpServerPrimitive? primitive)
    {
        return primitive is McpServerTool tool && Contains(tool.ProtocolTool.Name, tool.ProtocolTool.Annotations?.ReadOnlyHint);
    }
}
