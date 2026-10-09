using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

// A WorkOS connect_application_secret. Secret is present only in the create response.
public sealed record WorkOsClientSecret(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("secret")] string? Secret,
    [property: JsonPropertyName("secret_hint")] string? SecretHint,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt);
