using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum MachineCredentialKind
{
    ApiKey = 0,
    Certificate,
    CertificateAndApiKey,
}
