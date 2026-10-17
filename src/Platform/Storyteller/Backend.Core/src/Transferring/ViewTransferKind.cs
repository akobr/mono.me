using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller.Transferring;

[JsonConverter(typeof(StringEnumConverter))]
public enum ViewTransferKind
{
    Export = 0,
    Import,
}
