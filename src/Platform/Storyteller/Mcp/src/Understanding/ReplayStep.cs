using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// One merge of a replay. Order is the step's Order in the composition, null for a step the replay adds (a template the
// view does not have yet). Content is null for a Missing source, which applies nothing.
public sealed record ReplayStep(int? Order, string SourceId, int Node, JObject? Content);
