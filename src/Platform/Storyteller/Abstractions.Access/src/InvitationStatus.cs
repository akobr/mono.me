using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace _42.Platform.Storyteller;

[JsonConverter(typeof(StringEnumConverter))]
public enum InvitationStatus
{
    Pending = 0,
    Accepted,
    Declined,
    Revoked,

    // Never stored. A pending invitation past its ExpiresAt is reported as expired.
    Expired,
}
