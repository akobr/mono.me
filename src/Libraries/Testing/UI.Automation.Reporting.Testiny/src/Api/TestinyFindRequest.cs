using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// The body of POST /<entity>/find (DataReadParams): the entities of the ids in the project, ids only; omitForbidden
// leaves out the entities the API key may not read instead of failing the request.
internal sealed record TestinyFindRequest
{
    [JsonPropertyName("ids")]
    public required IReadOnlyList<long> Ids { get; init; }

    [JsonPropertyName("filter")]
    public TestinyProjectFilter? Filter { get; init; }

    [JsonPropertyName("idOnly")]
    public bool? IdOnly { get; init; }

    [JsonPropertyName("omitForbidden")]
    public bool? OmitForbidden { get; init; }

    [JsonPropertyName("pagination")]
    public TestinyPagination? Pagination { get; init; }
}
