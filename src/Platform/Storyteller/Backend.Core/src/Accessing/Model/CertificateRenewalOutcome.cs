using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller.Accessing.Model;

[JsonConverter(typeof(StringEnumConverter))]
public enum CertificateRenewalOutcome
{
    Success,
    AlreadyRenewed,
    NotInRenewalWindow,
    NotFound,
}
