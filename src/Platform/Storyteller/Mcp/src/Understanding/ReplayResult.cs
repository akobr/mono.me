using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// The document a replay produced; every value carries the step that set it.
public sealed class ReplayResult(JObject content, IReadOnlyList<ReplayStep> steps)
{
    public JObject Content { get; } = content;

    public ReplayStep? OriginOf(JToken token)
    {
        return token.Annotation<CompositionReplay.Mark>() is { } mark ? steps[mark.Step] : null;
    }
}
