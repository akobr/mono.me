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

    // Configuration reads plus key/value secrets; delivered to machines under the project's mTLS rule.
    SecretsRead = 6,
    SecretsReadWrite = 7,
}
