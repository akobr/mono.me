using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum MachineAccessScope
{
    AnnotationRead = 0,
    ConfigurationRead,
    DefaultRead,
    AnnotationReadWrite,
    ConfigurationReadWrite,
    DefaultReadWrite,
}
