using System.Text.Json.Serialization;

namespace _42.Platform.Storyteller;

// The subset of the WorkOS user object Storyteller reads.
public sealed record WorkOsUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName);
