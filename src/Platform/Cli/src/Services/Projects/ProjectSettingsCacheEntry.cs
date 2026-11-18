using System;
using System.Text.Json.Serialization;

namespace _42.Platform.Cli.Services.Projects;

internal sealed record ProjectSettingsCacheEntry
{
    // The API address (general.baseUrl, or STORYTELLER_ENDPOINT of a machine).
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; init; } = string.Empty;

    // <organization>.<project>
    [JsonPropertyName("project")]
    public string Project { get; init; } = string.Empty;

    [JsonPropertyName("etag")]
    public string? ETag { get; init; }

    // When the API answered (200 or 304) last.
    [JsonPropertyName("fetchedAt")]
    public DateTimeOffset FetchedAt { get; init; }

    // When a read failed last (offline, a timeout, a refusal); for RetryAfterFailure no request is made again.
    [JsonPropertyName("failedAt")]
    public DateTimeOffset? FailedAt { get; init; }

    [JsonPropertyName("settings")]
    public ProjectSettingsCacheValues Settings { get; init; } = new();

    // Whether the API answered settings once: an entry may record a failed read only.
    [JsonIgnore]
    public bool HasSettings => ETag is not null;

    public bool Matches(string baseUrl, string project)
    {
        return string.Equals(BaseUrl.TrimEnd('/'), baseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            && string.Equals(Project, project, StringComparison.Ordinal);
    }
}
