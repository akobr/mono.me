using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

// How a caller reached the API; secret values go to machines only over mTLS when the project says so.
[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueChannel
{
    User = 0,
    MachineMtls,
    MachineToken,
    System,
}
