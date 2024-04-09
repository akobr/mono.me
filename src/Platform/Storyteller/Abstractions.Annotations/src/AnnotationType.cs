using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum AnnotationType
{
    Responsibility = 0,
    Unit,
    Subject,
    Usage,
    Context,
    Execution,
}
