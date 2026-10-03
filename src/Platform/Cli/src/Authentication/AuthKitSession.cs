using System;
using System.Text.Json.Serialization;

namespace _42.Platform.Cli.Authentication;

// What sform keeps after an AuthKit sign-in. The refresh token rotates on every use.
public sealed record AuthKitSession
{
    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("access_token_exp")]
    public DateTimeOffset AccessTokenExpiresAt { get; init; }

    [JsonPropertyName("user")]
    public required AuthKitSessionUser User { get; init; }

    [JsonPropertyName("organization_id")]
    public string? OrganizationId { get; init; }
}
