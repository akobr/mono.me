using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// The answer of POST /testrun/find.
internal sealed record TestinyTestRunFindResult
{
    [JsonPropertyName("data")]
    public List<TestinyTestRun> Data { get; init; } = [];
}
