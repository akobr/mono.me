using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// The body of POST /testrun/find (DataReadParams): the runs of a project with a title, newest first, with their
// descriptions (omitLargeValues false, as a description may count as a large value).
internal sealed record TestinyTestRunFindRequest
{
    [JsonPropertyName("filter")]
    public required TestinyTestRunFilter Filter { get; init; }

    [JsonPropertyName("order")]
    public IReadOnlyList<TestinySortOrder>? Order { get; init; }

    [JsonPropertyName("omitLargeValues")]
    public bool? OmitLargeValues { get; init; }

    [JsonPropertyName("pagination")]
    public TestinyPagination? Pagination { get; init; }
}
