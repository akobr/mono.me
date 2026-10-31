using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

internal sealed record TestinyFindMeta
{
    [JsonPropertyName("count")]
    public int Count { get; init; }

    [JsonPropertyName("offset")]
    public int Offset { get; init; }

    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    [JsonPropertyName("totalCount")]
    public int? TotalCount { get; init; }
}
