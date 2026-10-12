using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

// Which keys a listing returns: the active ones (default), the soft-deleted ones, or both.
[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueState
{
    Active = 0,
    Deleted,
    All,
}
