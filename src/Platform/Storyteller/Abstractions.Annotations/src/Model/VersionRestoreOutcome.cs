using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum VersionRestoreOutcome
{
    // The content of FromVersion is stored as the new version Version.
    Restored = 0,

    // FromVersion is the current version, or the current version Version has its content: nothing was written.
    Unchanged,
}
