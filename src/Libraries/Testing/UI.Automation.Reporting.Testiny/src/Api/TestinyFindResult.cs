using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// The answer of POST /<entity>/find: { "data": [ ... ], "meta": { "count", "offset", "limit", "totalCount" } }.
internal sealed record TestinyFindResult
{
    [JsonPropertyName("data")]
    public List<TestinyEntityId> Data { get; init; } = [];

    [JsonPropertyName("meta")]
    public TestinyFindMeta? Meta { get; init; }
}
