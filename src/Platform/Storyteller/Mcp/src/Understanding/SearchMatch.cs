using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// A configuration that matches. With content filters it names the value: its pointer in the stored document, the value
// itself (binding expressions as stored), or for a large object or array only its ValueKind.
public sealed record SearchMatch(string AnnotationKey, long Version)
{
    public string? Pointer { get; init; }

    public JToken? Value { get; init; }

    public string? ValueKind { get; init; }
}
