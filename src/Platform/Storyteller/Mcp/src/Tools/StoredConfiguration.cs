using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Tools;

// Content is null when nothing is stored (Version 0).
public sealed record StoredConfiguration(long Version, JObject? Content);
