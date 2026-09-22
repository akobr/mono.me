using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum DiffChangeType
{
    Unchanged = 0,
    Addition = 1,
    Deletion = 2,
}
