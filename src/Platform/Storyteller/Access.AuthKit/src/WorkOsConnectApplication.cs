using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

// The subset of the WorkOS connect_application object Storyteller reads.
public sealed record WorkOsConnectApplication(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("client_id")] string ClientId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("organization_id")] string? OrganizationId,
    [property: JsonPropertyName("scopes")] IReadOnlyList<string>? Scopes);
