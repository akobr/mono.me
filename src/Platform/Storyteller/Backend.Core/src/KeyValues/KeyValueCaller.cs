using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller.KeyValues;

[JsonConverter(typeof(StringEnumConverter))]
public enum KeyValueChannel
{
    User = 0,
    MachineMtls,
    MachineToken,
}

// The author of a write, and what the caller may do with secrets. The API decides the rights; the service enforces them.
public sealed record KeyValueCaller(string Author, bool CanReadSecrets, bool CanWriteSecrets, KeyValueChannel Channel = KeyValueChannel.User)
{
    public static KeyValueCaller WithSecrets(string author) => new(author, CanReadSecrets: true, CanWriteSecrets: true);

    public static KeyValueCaller WithoutSecrets(string author) => new(author, CanReadSecrets: false, CanWriteSecrets: false);
}
