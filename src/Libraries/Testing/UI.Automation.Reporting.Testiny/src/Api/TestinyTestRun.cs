using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// A classic test run (TestRun of the OpenAPI document 1.42.2). Unset values are not sent, so an update changes only what
// it sets; _etag is required by every update.
public sealed record TestinyTestRun
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    // 1 to 200 characters.
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("project_id")]
    public long? ProjectId { get; init; }

    // Markdown when it starts with the header line TestinyMarkdown.Header.
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("testplan_id")]
    public long? TestPlanId { get; init; }

    [JsonPropertyName("is_closed")]
    public bool? IsClosed { get; init; }

    [JsonPropertyName("closed_at")]
    public DateTimeOffset? ClosedAt { get; init; }

    [JsonPropertyName("_etag")]
    public string? ETag { get; init; }
}
