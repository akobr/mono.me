using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace _42.Platform.Cli.Authentication;

// The answer of GET v1/auth/configuration, stored next to access.default.json.
public sealed record AuthenticationDiscoveryCache
{
    [JsonPropertyName("baseUrl")]
    public required string BaseUrl { get; init; }

    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    [JsonPropertyName("clientId")]
    public required string ClientId { get; init; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; init; }

    [JsonPropertyName("scopes")]
    public IReadOnlyList<string>? Scopes { get; init; }

    [JsonPropertyName("authKitDomain")]
    public string? AuthKitDomain { get; init; }
}
