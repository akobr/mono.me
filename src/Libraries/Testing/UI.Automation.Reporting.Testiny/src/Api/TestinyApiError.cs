using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// The body of a 4xx or 5xx answer (ApiError), e.g. { "code": "API_INVALID_INPUT_DATA", "message": "...", "reqid": "..." }.
public sealed record TestinyApiError
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("status")]
    public int? Status { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("reqid")]
    public string? RequestId { get; init; }
}
