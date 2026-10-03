using System.Text.Json.Serialization;

namespace _42.Platform.Cli.Authentication;

public sealed record AuthKitSessionUser(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("email")] string? Email,
    [property: JsonPropertyName("first_name")] string? FirstName,
    [property: JsonPropertyName("last_name")] string? LastName);
