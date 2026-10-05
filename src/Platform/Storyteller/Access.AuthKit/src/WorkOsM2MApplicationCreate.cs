using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

// Body of POST /connect/applications for a machine-to-machine application.
public sealed record WorkOsM2MApplicationCreate(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("organization_id")] string OrganizationId,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string> Scopes)
{
    [JsonPropertyName("application_type")]
    public string ApplicationType => "m2m";

    // Storyteller machines act for customer projects, not as an app the WorkOS team runs.
    [JsonPropertyName("is_first_party")]
    public bool IsFirstParty => false;
}
